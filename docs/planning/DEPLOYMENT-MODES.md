# Deployment Modes: From Embedded Single-Player to Hyper-Scaled Multi-Node

> **Type**: Design
> **Status**: Active
> **Created**: 2026-03-14
> **Last Updated**: 2026-03-14
> **North Stars**: #1, #3, #4
> **Related Plugins**: Mesh, State, Messaging, Connect, Actor, Orchestrator, Location, Worldstate, Realm
> **Prerequisites**: [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md), [SELF-HOSTED-DEPLOYMENT.md](SELF-HOSTED-DEPLOYMENT.md)

## Summary

Catalogs the four deployment modes that a game built on Bannou can target (embedded single-player, non-dedicated player-hosted, dedicated single-node, hyper-scaled multi-node) and maps each to existing architecture, infrastructure backend selection, and identified gaps. All four modes use the same binary and codebase; the deployment mode is a configuration choice. Three modes work today or require only mechanical implementation; distributed world simulation for the non-dedicated and hyper-scaled modes represents the primary architectural gap requiring new design work in Actor regional affinity, Location partitioning, and node-aware mesh routing.

---

## Problem Statement

A game studio building on Bannou may need to ship four distinct hosting experiences from the same codebase:

| Mode | Player Experience | Infrastructure Constraint |
|------|------------------|--------------------------|
| **Embedded** | Everything runs inside the game process. No separate server, no network, no external dependencies. | Zero external processes. Must work on Android, desktop, console. |
| **Non-Dedicated** | "I'm playing and you join my game." Host player's machine runs the server while they play. Remote players connect and can be anywhere in the world, including different dimensions. | Must not suffer "stay near the host" limitations. World simulation should distribute across participants when beneficial. |
| **Dedicated Single-Node** | Traditional game server hosting (Nitrado, GGPortal, VPS). Efficient with memory and CPU. | Cost-conscious. Async, cache-friendly, minimal resource waste. |
| **Hyper-Scaled Multi-Node** | 1000+ concurrent players, potentially tens of thousands. Services distributed across many nodes. | Horizontal scaling. Actor pool auto-scaling. Multi-node WebSocket broadcast. |

Additionally, a studio must be able to create distinct build artifacts for anti-piracy:
- **Single-player/non-dedicated client**: Can play locally and host for friends. Cannot act as a standalone dedicated server.
- **Dedicated server build**: Headless. No game assets, no rendering code. Cannot be used as a game client.

This is a **build pipeline concern**, not a Bannou architecture concern. Bannou provides both compositions; the studio decides which binaries ship in which SKU.

---

## The Four Modes

### Infrastructure Backend Matrix

Every mode uses the same services. Only the infrastructure backends change:

| Component | Embedded | Non-Dedicated | Dedicated | Hyper-Scaled |
|-----------|----------|---------------|-----------|-------------|
| **State (persistence)** | InMemory + SQLite | InMemory + SQLite | Redis + MySQL | Redis cluster + MySQL cluster |
| **Messaging** | DirectDispatch | DirectDispatch | RabbitMQ | RabbitMQ cluster |
| **Mesh routing** | Direct DI dispatch | Local omnipotent | Local omnipotent | Distributed (Orchestrator topology) |
| **Connect** | Not loaded | Single instance (+ relay for NAT traversal) | Single instance | N instances, broadcast mesh |
| **Telemetry** | NullTelemetryProvider | Optional | Full OpenTelemetry | Full OpenTelemetry |
| **Orchestrator** | Not loaded | Not loaded | Optional | Required |

Configuration toggles:

```bash
# Embedded
STATE_USE_SQLITE=true          # MySQL stores -> SQLite, Redis stores -> InMemory
MESSAGING_USE_DIRECT_DISPATCH=true  # RabbitMQ -> DirectDispatch (zero-overhead IEventConsumer dispatch)
# Mesh: direct DI dispatch (no HTTP, planned)
# Connect: not loaded

# Non-Dedicated (sidecar)
STATE_USE_SQLITE=true
MESSAGING_USE_DIRECT_DISPATCH=true  # Zero-overhead event dispatch (single-node)
BANNOU_HTTP_WEB_HOST_PORT=5012

# Dedicated Single-Node
# (defaults -- Redis + MySQL + RabbitMQ)
BANNOU_HTTP_WEB_HOST_PORT=5012

# Hyper-Scaled Multi-Node
# (defaults + Orchestrator topology preset)
CONNECT_MULTINODE_BROADCAST_MODE=Both
# Orchestrator pushes service mappings to Mesh
```

Note: `STATE_USE_SQLITE=true` activates **both** alternatives simultaneously. MySQL-backed stores become SQLite; Redis-backed stores become InMemory. All state store interfaces (`IStateStore<T>`, `ICacheableStateStore<T>` with sorted sets/sets/hashes, `IQueryableStateStore<T>` with LINQ) have full feature parity across all backends. No services need to be disabled in any deployment mode.

---

### Mode 1: Embedded (In-Process)

**Status**: Aspirational, ~2-3 days of implementation. See [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md).

The game binary contains Bannou as a library. No separate process, no HTTP, no network. `IBannouClient.Character.GetAsync()` resolves to a direct method call through DI.

```
Game Process
  +-- EmbeddedBannouHost (in-process)
      +-- PluginLoader (same assembly loading as cloud mode)
      +-- IServiceProvider (generic host, no ASP.NET Core)
      +-- SQLite + InMemory backends
      +-- Background workers (BackgroundService, not ASP.NET-specific)
      +-- Direct DI dispatch (ICollectionClient -> ICollectionService)
```

**What exists**:
- Infrastructure libs have full embedded backends (SQLite, InMemory, InMemory locks)
- ASP.NET Core coupling is phantom -- plugins only need `IServiceProvider` (see BANNOU-EMBEDDED.md investigation)
- Generated controllers become dead code (never instantiated, zero runtime cost)
- All background workers use `Microsoft.Extensions.Hosting.BackgroundService` (not ASP.NET Core)

**Remaining work** (see BANNOU-EMBEDDED.md for details):

| Layer | What | Effort |
|-------|------|--------|
| Plugin decoupling | `ConfigureApplication(WebApplication)` -> `ConfigureApplication(IServiceProvider)` across ~26 files | Half day |
| Client generation | Add direct dispatch branch to NSwag template + `InvokeDirectAsync` helper | 1-2 days |
| Embedded host | `EmbeddedBannouHost` composition root (2 new files) | Half day |
| Service verification | Already verified -- all core services work with SQLite + InMemory | None |

**Services skipped in embedded mode**: Connect, Auth, Mesh (HTTP API), Telemetry, Orchestrator, Website, Voice, Broadcast. These are either network-facing (no network) or operational (no infrastructure to orchestrate).

---

### Mode 2: Non-Dedicated (Player-Hosted Sidecar)

**Status**: Active, basic model works today. Distributed world simulation is an open gap. See [SELF-HOSTED-DEPLOYMENT.md](SELF-HOSTED-DEPLOYMENT.md).

The game ships with a BannouServer binary. Host player launches game; server starts as a background process. Remote players connect via WebSocket to the host's IP.

```
Host Machine
+----------------------------------------------+
| Game.exe (rendering engine)                   |
|   SDK --WebSocket--> localhost:5012            |
+----------------------------------------------+
| BannouServer (background process)             |
|   Connect (L1) -- binary routing --> Services |
|   State: SQLite + InMemory                    |
|   Messaging: InMemory                         |
|   Mesh: Local omnipotent routing              |
+----------------------------------------------+

Remote Player
+----------------------------------------------+
| Game.exe (rendering engine)                   |
|   SDK --WebSocket--> host-ip:5012             |
+----------------------------------------------+
```

**What works today**:
- SQLite persistence (implemented, [#442](https://github.com/beyondimmersion/bannou-service/issues/442))
- InMemory messaging with retry/buffer mechanisms
- Connect handles WebSocket connections from local and remote players
- Selective plugin loading (e.g., ~15 plugins for a factory game out of 76)
- Workshop lazy evaluation (production computes retroactively on query, server doesn't need to tick)

**What works but could be better**:
- All service calls route via HTTP to localhost (~0.5-2ms overhead). In-process mesh routing (Approach B in SELF-HOSTED-DEPLOYMENT.md) would eliminate this, but it's irrelevant for most game types.
- Windows firewall prompts when starting a local HTTP server. Mitigations: loopback binding, installer firewall registration, future Unix domain socket support.

**What doesn't work: Distributed world simulation** -- see [dedicated section below](#the-hard-problem-distributed-world-simulation). For the peer delegation topology (star/host-authority), NAT relay via Connect, and event propagation model, see [Distributed Sidecar Topology](#distributed-sidecar-topology-peer-delegation-via-connect).

---

### Mode 3: Dedicated Single-Node

**Status**: Production-ready. This is Bannou's default deployment mode.

Standard dedicated server: Redis + MySQL + RabbitMQ. Full service stack. Efficient through async architecture, selective plugin loading, and Workshop lazy evaluation.

```
Dedicated Server (VPS / Hosting Provider)
+----------------------------------------------+
| BannouServer                                  |
|   Connect (L1) -- binary routing --> Services |
|   State: Redis + MySQL                        |
|   Messaging: RabbitMQ                         |
|   Mesh: Local omnipotent routing              |
+----------------------------------------------+
      ^          ^          ^
      |          |          |
   Player A   Player B   Player C
   (WebSocket) (WebSocket) (WebSocket)
```

**Memory/CPU efficiency**:
- Plugin loading is selective: a factory game loading 15/76 plugins uses dramatically less memory than a full MMO deployment
- Workshop lazy evaluation: production chains compute on query, not on tick. Server sleeps between player interactions.
- All service methods are async (ASP.NET Core Kestrel + async/await throughout)
- Asset service uses pre-signed MinIO/S3 URLs -- clients download directly from object storage, binary data never passes through the game server
- Save-Load uses two-tier storage (Redis hot cache -> MinIO cold storage). Inactive saves get evicted from Redis.
- Music compositions are deterministic: same seed -> same output -> Redis cache hit

**No gaps. No remaining work.**

---

### Mode 4: Hyper-Scaled Multi-Node

**Status**: Architecture designed, partially implemented.

Services distributed across N nodes. Orchestrator manages topology. Mesh routes between nodes. Connect instances form a WebSocket broadcast mesh. Actor pool nodes scale horizontally for NPC brain execution.

```
                    Load Balancer
                    /     |     \
            Connect-1  Connect-2  Connect-3
               |          |          |
               +----Broadcast Mesh---+
               |          |          |
          +----+----+ +---+----+ +--+-----+
          | Auth    | | Game   | | Actor   |
          | Account | | Found. | | Pool-1  |
          | Chat    | | (L2)   | | Pool-2  |
          | (L1)    | |        | | Pool-3  |
          +---------+ +--------+ +---------+
                                     |
                                ActorConnectionManager
                                (WebSocket -> Game Servers)
```

**What works today**:
- Orchestrator understands layer-aware topologies (presets with named nodes and layer assignments)
- Mesh supports 5 load-balancing algorithms, circuit breakers, endpoint caching
- Connect broadcast mesh relays client events across N instances (`CONNECT_MULTINODE_BROADCAST_MODE=Both`)
- Actor pool mode distributes NPC brains across dedicated nodes
- Service hierarchy enforced at startup (L0 -> L1 -> L2 -> L3 -> L4 -> L5)

**Open issues**:

| Issue | Description | Status |
|-------|-------------|--------|
| [#318](https://github.com/beyondimmersion/bannou-service/issues/318) | Actor pool auto-scaling (scale-up/down based on utilization) | Open |
| [#393](https://github.com/beyondimmersion/bannou-service/issues/393) | Actor migration between pool nodes without state loss (~100-200ms handoff) | Open |
| [#409](https://github.com/beyondimmersion/bannou-service/issues/409) | Game Engine <-> Actor WebSocket transport via Connect Internal mode | Open |
| [#552](https://github.com/beyondimmersion/bannou-service/issues/552) | Orchestrator blue-green deployment support | Open |
| [#406](https://github.com/beyondimmersion/bannou-service/issues/406) | Entity presence tracking (prerequisite for #409 location-keyed routing) | Open |

The 1000-player dedicated server works today with manual topology configuration. 10,000+ players requires the Actor auto-scaling (#318) and migration (#393) work.

---

## The Actor Regional Affinity Design

**Source**: [#409](https://github.com/beyondimmersion/bannou-service/issues/409) (Game Engine <-> Actor WebSocket transport via Connect Internal mode)

This design is central to both non-dedicated and hyper-scaled modes. It describes how Actor pool nodes maintain persistent WebSocket connections to game server instances, keyed by location.

### Architecture

Each Actor pool node runs a singleton **ActorConnectionManager** background service that manages a route table:

```
Route Table: locationId -> { PeerGuid, InitializationMode, LastUsed, Source }

Actor Pool Node "EastDistrict"
  2,000 actors (characters in eastern locations)
  Route table:
    location-east-market   -> { peer: game-server-1, mode: Immediate }
    location-east-docks    -> { peer: game-server-1, mode: Lazy }
    location-east-temple   -> { peer: game-server-2, mode: Lazy }
    location-west-tavern   -> { peer: game-server-3, mode: None }

  Maintained WebSocket connections: 2-3 (not 2,000)
```

All actors whose characters reside in the same location share a single WebSocket connection to that location's game server. With 10,000 actors on a single pool node, instead of per-actor connection state, the node maintains **one route per unique game server location**.

### Connection Initialization Modes

| Mode | Behavior | Use Case |
|------|----------|----------|
| **Lazy** | Route recorded in database. WebSocket established on first `TrySendAsync` miss. | Default. Most locations start cold. |
| **Immediate** | Route recorded AND WebSocket established immediately. | Hot locations (player-occupied areas). |
| **None** | Route marked as "no WebSocket." Always falls back to mesh HTTP. | Game server opts out of WebSocket transport for this location. |

### Route Discovery

Two mechanisms populate the route table:

1. **Perception Event Learning** (automatic, lazy): When perception events arrive from game servers, they carry `sourcePeerGuid` and `locationId`. The connection manager observes these and lazily populates routes.

2. **Manual Registration** (explicit): `/actor/connection/register` and `/actor/connection/unregister` endpoints allow game servers or Orchestrator to explicitly manage routes with specific modes and TTLs.

### Transport Fallback (3-Tier)

```
Actor needs to send action to game server:
  1. Check route table for character's locationId
  2. If route exists AND WebSocket is warm:
       -> Send via WebSocket (fastest, sub-ms)
  3. If route exists but WebSocket is cold:
       -> Queue WebSocket warm-up (background)
       -> Fall back to mesh HTTP (baseline, 0.5-2ms)
  4. If no route exists:
       -> Mesh HTTP (universal baseline)
  5. If mesh fails:
       -> RabbitMQ broadcast (last resort)
```

### Location Affinity for Actor Placement

The natural extension: when spawning an actor for a character in a specific location, prefer the pool node that already has a warm route to that location's game server. This keeps the connection count small while maximizing WebSocket utilization.

This does NOT mean actors are statically pinned to locations. Characters move. When a character moves to a new location, the actor's route changes on the next send. If the new location is managed by a different pool node, actor migration ([#393](https://github.com/beyondimmersion/bannou-service/issues/393)) relocates the actor to the optimal node.

---

## The Hard Problem: Distributed World Simulation

### What It Means

Distributed world simulation means different parts of the game world run on different machines. Player A's machine simulates Region X. Player B's machine simulates Region Y. Entities crossing between regions trigger state migration.

This is distinct from:
- **Multiple players on one server** (works today -- all state centralized)
- **Actor pool distribution** (works today -- compute distributed, state centralized)
- **Multiple realms on one server** (works today -- Worldstate is realm-scoped, Character is realm-partitioned)

### Current Architecture: Centralized State, Distributed Compute

```
                    All State (Redis/MySQL/SQLite)
                              |
            +-----------------+-----------------+
            |                 |                 |
        Actor Pool-1      Actor Pool-2      Actor Pool-3
        (NPC brains)      (NPC brains)      (NPC brains)
```

Services scale by adding pool nodes for compute-intensive tasks (Actor brains). But all state -- Location trees, Worldstate clocks, Character positions, Inventory contents -- lives in centralized stores. Every query from every node hits the same state backend.

This works for cloud deployments where state backends are high-performance network services (Redis cluster, MySQL cluster). It works for single-node deployments where everything is co-located. It even works for non-dedicated hosting where the host machine runs all state in SQLite + InMemory.

### Where It Falls Short

**Non-dedicated with distant players**: If Player A hosts and Player B is in a different dimension, all of Dimension B's simulation runs on Player A's CPU. For small player counts (2-8), this is likely acceptable -- the dormant/stirring/awakened actor gradient keeps NPC cost proportional to player proximity. But it's not optimal.

**Hyper-scaled with geographic spread**: At 1000+ players spread across many world regions, centralized state becomes a bottleneck. Even with Redis cluster, every Location query for every NPC action hits the same data plane.

**True distributed non-dedicated**: Shane's vision of "anchors" where remote player machines contribute simulation capacity requires state to live near where it's used, not in a central store.

### What Would Be Needed

Distributed world simulation requires extensions in three areas. None contradict the existing architecture; they extend it.

#### 1. Region-to-Node Affinity

A mechanism to register which node is authoritative for which regions/realms.

**Where it lives**: Orchestrator (L3), which already manages topology presets and service-to-node mappings.

**What it adds**: A new mapping dimension beyond service -> node. Now: service + region -> node.

```
Current Orchestrator mappings:
  "auth"      -> node-1
  "character" -> node-2
  "actor"     -> [pool-1, pool-2, pool-3]

Extended with region affinity:
  "actor" + realm:"Overworld" + region:"East" -> pool-1
  "actor" + realm:"Overworld" + region:"West" -> pool-2
  "actor" + realm:"Nether"                    -> pool-3
```

**Impact on existing design**: Orchestrator already pushes mappings to Mesh via `IServiceMappingReceiver`. The mapping model needs a region dimension. Mesh routing needs to accept an optional region hint when resolving endpoints.

#### 2. Location-Aware Mesh Routing

When a service call involves a specific location or realm, Mesh routes to the node that owns that region.

**Where it lives**: Mesh (L0).

**What it adds**: Generated clients would need to pass a location/realm context when the target service is region-sharded. Mesh resolves `("actor", region: "East")` -> `pool-1` instead of just `"actor"` -> `any pool node`.

**Design consideration**: Not all services need region-aware routing. Only services where state is regionally partitioned (Actor, potentially Location, Worldstate) would use this. Most services (Account, Auth, Item templates, Currency definitions) remain globally centralized.

#### 3. State Partitioning for Regional Services

The hardest piece. For services that become region-aware, their state stores need to be partitioned or replicated.

**Simple case -- Actor**: Actor state is already ephemeral (in-memory runner state, Redis assignments). Moving an actor between pool nodes is the existing migration design ([#393](https://github.com/beyondimmersion/bannou-service/issues/393)). No state store changes needed for Actor.

**Medium case -- Worldstate**: Each realm already has its own game clock and calendar. If different realms run on different nodes, Worldstate queries route to the node that owns that realm. State is naturally partitioned by realm (the store key includes `realmId`). The main change is ensuring the Worldstate background worker (clock tick) runs only on the node owning that realm.

**Hard case -- Location**: Location trees are hierarchical (realm -> region -> city -> building -> room). If regions within a realm run on different nodes, the Location service needs to know which subtree is authoritative where. This requires either:
- Location shard service (new) that maps location subtree roots to nodes
- Location service becomes shard-aware (existing service, new capability)

**Hard case -- Character position**: When a character moves from Region East (node-1) to Region West (node-2), their presence tracking, actor assignment, and subscription routing all need to migrate. This is closely related to Actor migration ([#393](https://github.com/beyondimmersion/bannou-service/issues/393)) and Entity Presence Tracking ([#406](https://github.com/beyondimmersion/bannou-service/issues/406)).

### Interaction with ActorConnectionManager (#409)

The Actor regional affinity design from [#409](https://github.com/beyondimmersion/bannou-service/issues/409) is the natural foundation for distributed world simulation:

- Actor pool nodes are already organized by location affinity
- The `ActorConnectionManager` route table maps `locationId` -> game server
- Adding region-to-node affinity is extending this mapping from "which game server" to "which game server AND which pool node"
- The 3-tier transport fallback (WebSocket -> Mesh -> RabbitMQ) works regardless of how nodes are assigned to regions

The key insight: **#409's location-keyed connection pooling is the same pattern needed for distributed world simulation, applied at the actor-to-game-server layer.** Extending it to the service-to-service layer (via Mesh region-aware routing) follows the same design.

### Phased Approach

Distributed world simulation is not all-or-nothing. It can be built incrementally:

**Phase 0 (works today)**: All state centralized. All players connect to one server. Non-dedicated host runs everything. Sufficient for 2-8 player games.

**Phase 1 (Actor affinity, #409)**: Actor pool nodes have location affinity. NPCs near Player A run on nodes near Player A's game server. State remains centralized. This is the largest single performance win -- NPC action dispatch via warm WebSocket instead of mesh HTTP.

**Phase 2 (Realm partitioning)**: Different realms can run on different nodes. Worldstate, Actor, and perception routing become realm-aware. Cross-realm events flow via messaging. This enables "Player A in Overworld, Player B in Nether" on different machines.

**Phase 3 (Region partitioning within a realm)**: Different regions within the same realm can run on different nodes. Location tree partitioning. Character migration on region crossing. This is the full distributed world simulation vision.

Each phase works independently. A game studio can ship with Phase 0 and upgrade to Phase 1 without architecture changes -- it's just Actor deployment configuration. Phase 2 requires Mesh routing changes. Phase 3 requires Location partitioning.

---

## Distributed Sidecar Topology: Peer Delegation via Connect

### Network Topology: Star (Host-Authority)

Three topologies were evaluated for 2-8 player distributed sidecar:

| Topology | Description | Verdict |
|----------|-------------|---------|
| **Chain** | Each node connects upstream/downstream, events propagate linearly | Rejected. Latency scales linearly with chain length. Any middle node dropping breaks the chain. Ordering is a nightmare. |
| **Full mesh** | Every node connects to every other (N-1 connections each) | Rejected for game state. No natural authority for conflict resolution — two players grabbing the same item simultaneously requires consensus (vector clocks, Raft). Appropriate for voice (lib-voice already does this with WebRTC), not for state mutations. |
| **Star (host-authority)** | One node is authoritative. All peers connect to it. Host serializes mutations, resolves conflicts, broadcasts results. | **Selected.** This is what Mode 2 already is. The "distributed" extension means the host can delegate computation to peers while staying authoritative for state. |

The star topology means:
- Host's Connect instance maintains one WebSocket connection to each peer's sidecar
- Every event/message from a peer goes to the host
- Host fans out to all other peers as needed
- Host resolves all conflicts (same item grabbed by two players → host decides)
- Peers contribute CPU (Actor pool workers, region simulation), not decisions

This is the natural extension of Mode 2's existing architecture. The host is already the BannouServer that all clients connect to. Adding peer delegation means some peers also run Bannou services (Actor, environment simulation) alongside their game client, and the host's mesh routes work to them.

### Peer Transport: Connect WebSocket Protocol

Each sidecar peer is a full Bannou node with its own Connect instance. Sidecar-to-sidecar connections use the existing binary WebSocket protocol (already extracted into an SDK). This means:

- Each peer registers with mesh as its own appId (e.g., `bannou-peer-{sessionId}`)
- Mesh routing table populates from peer connections, not from Orchestrator/Redis
- Cross-node service calls use the same generated clients, same mesh pipeline — transport is Connect WebSocket relay instead of HTTP-to-localhost
- The host node maintains the peer registry: when a peer connects, their appId and capability set is broadcast to all other peers

Discovery is lightweight: the host assigns peer roles at join time (e.g., "you simulate Region East"). No Orchestrator involvement — Orchestrator manages container deployments (Docker/Portainer/K8s), which is irrelevant for peer-to-peer game sessions.

### NAT Relay: Connect Relay Mode

**Problem**: For internet play across NATs, the host needs to be reachable. LAN play is trivial. Port-forwarding has bad UX. UPnP is unreliable.

**Solution**: A minimal cloud-hosted Connect instance acting as a relay. Not a separate TURN server or new plugin — Connect itself in relay mode.

```
Cloud (minimal footprint):
  Connect relay instance (forwards WebSocket frames only)
      ↕ WebSocket          ↕ WebSocket          ↕ WebSocket
  Host sidecar          Peer 1               Peer 2
  (authority)           (delegated)          (delegated)
  Full Bannou stack     Actor workers        Actor workers
```

The relay Connect doesn't run game services. Its configuration:

```bash
BANNOU_SERVICES_ENABLED=false
CONNECT_SERVICE_ENABLED=true
STATE_USE_SQLITE=true              # L0 state: InMemory + SQLite (unused but present)
MESSAGING_USE_DIRECT_DISPATCH=true # L0 messaging: local-only (unused but present)
```

**Why not a separate lib-relay plugin?** A relay plugin would need to reimplement or duplicate Connect's WebSocket connection management, binary protocol handling, session lifecycle, and multi-node broadcast — which is the hard part of Connect. The binary protocol SDK helps with framing, but the connection lifecycle is the expensive code. Splitting relay from Connect creates shared ownership of the most latency-sensitive path in the system.

**Why accept idle L0 plugins?** L0 infrastructure on a relay node costs nearly nothing:
- lib-state: InMemory backend — a few empty `ConcurrentDictionary` instances. Zero I/O.
- lib-messaging: DirectDispatch — no broker, no connections. Idle.
- lib-mesh: Local omnipotent routing — a static routing table pointing at itself.
- lib-telemetry: NullTelemetryProvider (default when disabled).

Total overhead is approximately 5MB of empty in-memory structures. On a cloud relay instance that does nothing but forward WebSocket frames, this is noise. The hierarchy isn't violated — L0 plugins aren't optional, they're just idle. Same as a dedicated Actor pool node that has lib-state registered but never writes character data.

Connect already has relay capability: `CONNECT_MULTINODE_BROADCAST_MODE=Both` relays between Connect instances in cloud multi-node mode. The relay use case is the same code path with sidecar peers instead of other cloud Connect instances.

**When relay is NOT needed**: LAN play. Games where the host can be reached directly (port-forwarded, public IP). Only internet play across restrictive NATs requires the relay — and even then it's a single Connect instance, not a full Bannou deployment.

### Event Propagation Model

Each sidecar node uses DirectDispatch for local event delivery within its own process. Cross-node event propagation follows the star topology:

1. Peer produces an event (e.g., Actor completes an action)
2. DirectDispatch delivers to local IEventConsumer handlers
3. Peer sends the event to the host via its Connect WebSocket
4. Host receives, processes (may update authoritative state), fans out to other peers
5. Each receiving peer's DirectDispatch delivers to their local handlers

State synchronization follows events, not state replication. Each node is authoritative for its own assigned entities. When Node A's character does something that Node B needs to know about, the event propagates through the host and Node B updates its own local view. This avoids distributed consensus (CRDTs, Raft) entirely — the host serializes all mutations.

### Relationship to Existing Phased Approach

The distributed sidecar topology is orthogonal to the Phase 0-3 progression described in [The Hard Problem](#the-hard-problem-distributed-world-simulation):

| Phase | Cloud Multi-Node | Distributed Sidecar |
|-------|-----------------|-------------------|
| **0 (centralized)** | Single server, all state centralized | Host runs everything, peers are pure clients (Mode 2 today) |
| **1 (Actor affinity)** | Actor pool nodes with location affinity | Host delegates Actor simulation to peers by region |
| **2 (realm partitioning)** | Different realms on different cloud nodes | Different realms on different peer machines |
| **3 (region partitioning)** | Regions within a realm on different nodes | Regions within a realm on different peers |

The mechanism is the same (mesh routing with location/realm context). The transport differs (cloud uses HTTP/RabbitMQ between servers; sidecar uses Connect WebSocket between peers). The authority model differs (cloud can use distributed state backends for shared authority; sidecar uses host-authority star topology).

---

## Identified Gaps

### Gap 1: Embedded Mode Client Generation Template

**Affects**: Mode 1 (Embedded)
**Severity**: Blocking for embedded mode
**Reference**: [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)
**Effort**: 2-3 days

Generated mesh clients assume HTTP transport. Need a direct-dispatch branch in the NSwag template that resolves the service interface from DI and invokes directly. Requires modifying the generation template (frozen artifact -- needs explicit approval).

### Gap 2: Plugin System ASP.NET Core Decoupling

**Affects**: Mode 1 (Embedded)
**Severity**: Blocking for embedded mode
**Reference**: [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)
**Effort**: Half day (mechanical)

`IBannouPlugin.ConfigureApplication` takes `WebApplication` when it only needs `IServiceProvider`. ~26 files, 1-line change each.

### Gap 3: Actor Pool Auto-Scaling

**Affects**: Mode 4 (Hyper-Scaled)
**Severity**: Required for 10K+ players
**Reference**: [#318](https://github.com/beyondimmersion/bannou-service/issues/318)
**Effort**: Medium

Pool capacity management is manual. Need automated scale-up when utilization exceeds threshold and scale-down when idle. DI Listener pattern already designed (Actor defines `IActorPoolScaleListener`, Orchestrator implements).

### Gap 4: Actor Migration Between Pool Nodes

**Affects**: Mode 4 (Hyper-Scaled), Mode 2 (if distributed)
**Severity**: Required for location affinity and distributed world simulation
**Reference**: [#393](https://github.com/beyondimmersion/bannou-service/issues/393)
**Effort**: Medium

Moving a running actor from one pool node to another without state loss. V1 strategy: graceful migration with brief interruption (~100-200ms). Full state persistence, source-to-target handoff with rollback safety.

### Gap 5: ActorConnectionManager (Game Engine WebSocket Transport)

**Affects**: Mode 4 (Hyper-Scaled), Mode 2 (if distributed)
**Severity**: Major performance optimization for NPC-heavy games
**Reference**: [#409](https://github.com/beyondimmersion/bannou-service/issues/409), [#406](https://github.com/beyondimmersion/bannou-service/issues/406)
**Effort**: Medium-Large

Location-keyed WebSocket connection pool between Actor pool nodes and game servers. Includes route table, lazy/immediate/none initialization modes, perception event learning, and 3-tier transport fallback. Prerequisite: Entity Presence Tracking (#406).

### Gap 6: Region-Aware Mesh Routing

**Affects**: Mode 4 (Hyper-Scaled, Phase 2+), Mode 2 (if distributed)
**Severity**: Required for distributed world simulation
**Reference**: New design work needed
**Effort**: Medium

Mesh routing currently resolves `service name -> app-id -> endpoint`. For distributed world simulation, it needs an additional dimension: `service name + region -> app-id -> endpoint`. Generated clients for region-partitioned services would need to pass a location/realm context.

### Gap 7: Location Tree Partitioning

**Affects**: Mode 4 (Hyper-Scaled, Phase 3)
**Severity**: Required for intra-realm region distribution
**Reference**: New design work needed
**Effort**: Large

Location service becomes shard-aware: different subtrees of the location hierarchy are authoritative on different nodes. Requires mapping location subtree roots to nodes and routing Location queries accordingly. Character region-crossing triggers state migration.

### Gap 8: Connect Relay Mode for NAT Traversal

**Affects**: Mode 2 (Non-Dedicated, internet play across NATs)
**Severity**: Required for non-LAN distributed sidecar
**Reference**: See § "Distributed Sidecar Topology" above
**Effort**: Medium

Connect needs a minimal relay configuration where it forwards WebSocket frames between peers without running game services. The multi-node broadcast capability (`CONNECT_MULTINODE_BROADCAST_MODE=Both`) is the starting point — it already relays between Connect instances. The relay use case is the same code path with sidecar peers instead of cloud nodes. L0 plugins run with InMemory/DirectDispatch backends (idle, ~5MB overhead). No new plugin needed.

### Gap 9: Mesh Peer Discovery for Distributed Sidecar

**Affects**: Mode 2 (Non-Dedicated, distributed)
**Severity**: Required for peer delegation
**Reference**: See § "Distributed Sidecar Topology" above
**Effort**: Medium

When a sidecar peer connects to the host, mesh needs to learn the peer's appId and route service calls to it. Currently mesh populates routing from Orchestrator (Redis-backed) or static omnipotent routing. Distributed sidecar needs a lightweight peer registry: host assigns roles, peers register capabilities, mesh routes accordingly. No Orchestrator involvement.

### Gap 10: Orchestrator Blue-Green Deployment

**Affects**: Mode 4 (Hyper-Scaled)
**Severity**: Required for zero-downtime deployments at scale
**Reference**: [#552](https://github.com/beyondimmersion/bannou-service/issues/552)
**Effort**: Medium

Atomic topology switchover for distributed deployments. Not required for any other mode.

---

## Decisions

### D1: DirectDispatch Messaging for Embedded and Non-Dedicated Modes

**Status**: Decided (implemented)

`DirectDispatchMessageBus` (`MESSAGING_USE_DIRECT_DISPATCH=true`) is the recommended messaging backend for both embedded and non-dedicated deployments. It dispatches `TryPublishAsync` directly to `IEventConsumer.DispatchAsync`, eliminating the NativeEventConsumerBackend bridge layer that InMemoryMessageBus still carries. Zero serialization, zero intermediate subscription registry. For 2-8 player non-dedicated hosting and single-player embedded mode, this is optimal. Bundling RabbitMQ with a game client is too heavy for these models. InMemoryMessageBus remains available (`MESSAGING_USE_INMEMORY=true`) for testing and backward compatibility. See [DIRECT-DISPATCH-EVENTS.md](DIRECT-DISPATCH-EVENTS.md) for full design.

### D2: Anti-Piracy Is a Game-Side Concern

**Status**: Decided

Bannou provides the deployment flexibility; the studio decides which binaries ship in which SKU. The embedded binary has no listening socket (can't accept connections). The dedicated server binary has no game assets (can't be used as a client). The separation is in the build pipeline, not the architecture.

### D3: All State Store Interfaces Have Full Backend Parity

**Status**: Decided (verified)

`STATE_USE_SQLITE=true` activates both alternatives simultaneously: MySQL stores -> SQLite, Redis stores -> InMemory. `ICacheableStateStore<T>` (sorted sets, sets, hashes, counters) has full parity in the InMemory implementation. `IQueryableStateStore<T>` (LINQ) has full parity in SQLite via EF Core. No services need to be disabled in any deployment mode.

### D4: Workshop Lazy Evaluation Has Distributed Locking

**Status**: Decided (verified)

Concurrent access to Workshop materialization (e.g., two players querying the same production chain) is safe. Distributed locks prevent race conditions. This is not a deployment-mode-specific concern.

### D5: Actor Pool Nodes Should Have Location Affinity

**Status**: Decided (designed in #409)

Actor pool distribution should prefer location affinity over pure capacity-based distribution. This naturally groups actors with their game servers and minimizes the number of maintained WebSocket connections while maximizing transport performance. The ActorConnectionManager design (#409) is the implementation vehicle.

### D6: Distributed Sidecar Uses Star Topology (Host-Authority)

**Status**: Decided

Full mesh requires consensus for conflict resolution (wrong layer of complexity for 2-8 players). Chain topology has unacceptable latency and fragility. Star topology with host-authority matches Mode 2's existing architecture: host runs BannouServer, serializes mutations, resolves conflicts. The "distributed" part delegates computation (Actor brains, region simulation) to peers, not authority. Peers contribute CPU via the same Actor pool pattern used in cloud mode.

### D7: NAT Relay Is Connect in Minimal Configuration, Not a New Plugin

**Status**: Decided

A relay plugin would duplicate Connect's WebSocket connection management and binary protocol handling — the most complex and latency-sensitive code in the system. Idle L0 plugins on a relay node cost ~5MB of empty in-memory structures, which is negligible for a cloud relay instance that only forwards frames. Connect already has relay capability via multi-node broadcast mode. The relay use case is the same code path.

### D8: Distributed World Simulation Is a Phased Investment

**Status**: Proposed

Phase 0 (centralized, works today) is sufficient for most game types. Phase 1 (Actor affinity, #409) is the next priority. Phase 2 (realm partitioning) and Phase 3 (region partitioning) are future work, to be designed when Phase 1 is operational and profiling reveals centralized state as a bottleneck.

---

## Open Questions

### Q1: How Does Region-Aware Routing Propagate to Generated Clients?

For distributed world simulation (Phase 2+), some service calls need a region hint. How does this propagate through the generated client -> mesh -> routing pipeline?

Options:
- **Ambient context**: Thread/async-local `RegionContext` set by the caller, read by mesh routing. No client signature changes.
- **Explicit parameter**: Generated clients for region-aware services accept an optional `regionId`. Clean but changes client APIs.
- **Request body**: Region information is already in the request (e.g., `realmId` or `locationId`). Mesh inspects the request body to determine routing. Couples routing to payload structure.

### Q2: Which Services Become Region-Aware in Phase 2?

When realms can run on different nodes, which L2 services need realm-aware routing?

Candidates:
- **Actor**: Yes (actors are realm-scoped via character binding)
- **Worldstate**: Yes (game clock is per-realm)
- **Location**: Yes (location tree is per-realm)
- **Character**: Maybe (characters are realm-partitioned, but template data is global)
- **Seed**: Unlikely (seed data is per-entity, not per-realm)
- **All others**: Likely remain globally centralized

### Q3: What Happens to Cross-Region Events in Phase 3?

When regions within a realm run on different nodes, events published in Region East need to reach subscribers in Region West (e.g., a regional watcher that monitors the entire realm). Options:
- **RabbitMQ handles it**: Events are published to RabbitMQ topics as today. All nodes subscribe to realm-scoped topics. No change needed.
- **Event bridging**: Nodes only subscribe to their region's events. A bridge service forwards cross-region events. Reduces event volume per node.

RabbitMQ's existing fan-out model likely handles this without changes. The question is whether event volume at scale (100K+ NPCs publishing events) requires per-region topic segmentation.

### Q4: Non-Dedicated Mode -- When Is Centralized Simulation Insufficient?

For the sidecar model (host runs everything), at what player count or NPC density does centralized simulation become a problem? This determines whether Phase 2/3 distributed simulation is needed for non-dedicated hosting or only for hyper-scaled cloud.

Factors:
- Number of active NPCs (proportional to player count and proximity)
- Dormant/stirring/awakened gradient reduces NPC cost dramatically
- Workshop lazy evaluation eliminates production tick cost
- Modern desktop CPUs can likely handle hundreds of active NPC brains

If 2-8 players with hundreds of active NPCs is the target for non-dedicated, centralized simulation (Phase 0) may be permanently sufficient.

---

## Related Documents

### Planning & Design
- [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md) -- In-process service invocation design (Mode 1)
- [SELF-HOSTED-DEPLOYMENT.md](SELF-HOSTED-DEPLOYMENT.md) -- Sidecar server design (Mode 2/3)
- [BEHAVIOR-COMPOSITION.md](BEHAVIOR-COMPOSITION.md) -- Plan cache for 100K+ actors (Mode 4 optimization)
- [LOCATION-BOUND-PRODUCTION.md](LOCATION-BOUND-PRODUCTION.md) -- Workshop lazy evaluation (all modes)

### GitHub Issues
- [#409](https://github.com/beyondimmersion/bannou-service/issues/409) -- ActorConnectionManager design (location-keyed WebSocket transport)
- [#406](https://github.com/beyondimmersion/bannou-service/issues/406) -- Entity Presence Tracking (prerequisite for #409)
- [#318](https://github.com/beyondimmersion/bannou-service/issues/318) -- Actor pool auto-scaling
- [#393](https://github.com/beyondimmersion/bannou-service/issues/393) -- Actor migration between pool nodes
- [#552](https://github.com/beyondimmersion/bannou-service/issues/552) -- Orchestrator blue-green deployment
- [#442](https://github.com/beyondimmersion/bannou-service/issues/442) -- SQLite state store backend (implemented)

### FAQs
- [HOW-DOES-THE-MONOSERVICE-ACTUALLY-DEPLOY.md](../faqs/HOW-DOES-THE-MONOSERVICE-ACTUALLY-DEPLOY.md)
- [WHY-DOES-ORCHESTRATOR-EXIST-ALONGSIDE-KUBERNETES.md](../faqs/WHY-DOES-ORCHESTRATOR-EXIST-ALONGSIDE-KUBERNETES.md)

### Reference
- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) -- Layer rules and deployment modes
- [VISION.md](../reference/VISION.md) -- North Stars #1, #3, #4

---

*This document consolidates deployment mode design across BANNOU-EMBEDDED.md, SELF-HOSTED-DEPLOYMENT.md, and the Actor regional affinity design (#409) into a single reference. For game-type-specific service selection, see SELF-HOSTED-DEPLOYMENT.md § "The Game-Type Spectrum." For full embedded mode implementation details, see BANNOU-EMBEDDED.md.*
