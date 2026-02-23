# How Does One Binary Actually Deploy as Both a Monolith and Microservices?

> **Short Answer**: Every service is a plugin assembly loaded at startup based on environment variables. The same compiled binary runs all 48 services in one process for development, or one service per container in production. The code does not know or care how it is deployed.

---

## The Claim Sounds Impossible

Bannou claims to be a "monoservice" -- a single codebase that deploys as anything from a monolith to fully distributed microservices. This sounds like marketing language for "we have a monolith and we call it something fancy."

It is not. The deployment flexibility is real, and it works through three concrete mechanisms: plugin assembly loading, environment-based service activation, and infrastructure lib abstraction.

---

## Mechanism 1: Plugin Assembly Loading

Each of Bannou's 48 services is compiled into an independent .NET assembly (DLL). The `PluginLoader` discovers and loads these assemblies at startup based on configuration:

```bash
# Development: load everything
BANNOU_SERVICES_ENABLED=true

# Production node 1: auth cluster
BANNOU_SERVICES_ENABLED=false
ACCOUNT_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true

# Production node 2: game foundation
BANNOU_SERVICES_ENABLED=false
CHARACTER_SERVICE_ENABLED=true
REALM_SERVICE_ENABLED=true
SPECIES_SERVICE_ENABLED=true
LOCATION_SERVICE_ENABLED=true

# Production node 3: NPC brains
BANNOU_SERVICES_ENABLED=false
ACTOR_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=true
```

When a service is not enabled, its assembly is not loaded. Its controllers are not registered. Its DI services are not added to the container. Its event subscriptions are not created. It does not exist in the running process.

This is not a feature flag that disables code paths. The code is literally not loaded into memory. A container running only Actor and Behavior has no Character controller, no Realm state store, no Currency event subscriptions. The attack surface, memory footprint, and startup time reflect only what is enabled.

---

## Mechanism 2: Layer-Ordered Startup

The PluginLoader does not load assemblies randomly. It reads the `ServiceLayer` attribute from each service's `[BannouService]` attribute and loads them in hierarchy order:

1. **L0** (Infrastructure): state, messaging, mesh, telemetry
2. **L1** (App Foundation): account, auth, connect, permission, contract, resource
3. **L2** (Game Foundation): character, realm, species, location, ...
4. **L3** (App Features): asset, orchestrator, documentation, website
5. **L4** (Game Features): behavior, actor, matchmaking, voice, ...
6. **L5** (Extensions): third-party plugins

Within each layer, plugins load alphabetically. This guarantees that when a service's constructor runs, all services in lower layers are already registered in the DI container. An L2 service can safely inject L1 clients in its constructor because L1 loaded first.

This loading order is identical whether you run all 48 services in one process or one service per container. The single-service container just has fewer layers to load.

---

## Mechanism 3: Infrastructure Lib Abstraction

The reason services do not know how they are deployed is that they never make deployment-aware decisions. All infrastructure access goes through three abstraction layers:

### State (lib-state)

```csharp
var store = stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character);
await store.SaveAsync(characterId.ToString(), character);
```

The service does not know if Redis is running locally, in a sidecar container, or in a managed cloud service. It asks for a store and gets one. The connection string comes from environment configuration, not from code.

### Messaging (lib-messaging)

```csharp
await messageBus.PublishAsync("character.created", characterCreatedEvent);
```

The service does not know if RabbitMQ is in the same process (in-memory mode for testing), on a local Docker network, or in a cloud-managed cluster. It publishes an event and the infrastructure delivers it.

### Service Invocation (lib-mesh)

```csharp
var (status, response) = await authClient.ValidateTokenAsync(request);
```

This is where the monoservice magic happens. When all services run in one process, lib-mesh routes the call **in-process** -- it is a direct method invocation, no HTTP involved. When services are distributed across containers, lib-mesh routes via HTTP using YARP, with the target resolved from the Orchestrator's routing tables.

The service code is identical in both cases. The `authClient.ValidateTokenAsync()` call does not know whether Auth is in the same process or on a different continent. Lib-mesh handles discovery, routing, load balancing, circuit breaking, and retry logic transparently.

---

## What This Looks Like in Practice

### Local Development

```bash
# .env file
BANNOU_SERVICES_ENABLED=true
REDIS_CONNECTION_STRING=redis:6379
RABBITMQ_CONNECTION_STRING=amqp://guest:guest@rabbitmq:5672
```

One `docker-compose up`. One process runs all 48 services. Service calls are in-process. State is in a local Redis container. Events flow through a local RabbitMQ container. Total containers: 4 (app, Redis, RabbitMQ, MySQL).

A developer can set breakpoints in any service, trace a request from the WebSocket gateway through auth, through character creation, through event publication, all in one debugger session.

### CI/CD Testing

```bash
# Testing specific services
BANNOU_SERVICES_ENABLED=false
TESTING_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true
```

Only the services needed for the test scenario are loaded. The test binary starts in seconds, not minutes. State stores, messaging, and mesh work exactly as they do in development.

### Production (Distributed)

```bash
# Auth nodes (multiple instances behind load balancer)
BANNOU_SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true

# Game foundation nodes
BANNOU_SERVICES_ENABLED=false
CHARACTER_SERVICE_ENABLED=true
REALM_SERVICE_ENABLED=true
# ... etc

# NPC processing pool (auto-scaled)
BANNOU_SERVICES_ENABLED=false
ACTOR_SERVICE_ENABLED=true
```

Each node runs the same binary with different environment variables. Service calls go through HTTP via lib-mesh. The Orchestrator maintains routing tables that map service names to container endpoints. Mesh resolves `ICharacterClient` to whichever container is running the Character service.

Scaling a specific service means deploying more containers with that service enabled. The Orchestrator updates routing tables, mesh discovers the new endpoints, and load balancing distributes traffic. No code changes. No recompilation.

---

## Why Not Just Use Microservices?

The monoservice pattern provides something pure microservices cannot: **the same debugging experience at every scale**.

In a traditional microservices architecture, local development requires running N containers, configuring service discovery, managing N separate log streams, and debugging across process boundaries. The development experience is fundamentally different from production. Bugs that only appear in distributed deployment are invisible during development.

In Bannou, development is a monolith. Everything runs in one process. You can step through a request from WebSocket ingress to database write without leaving the debugger. When that same code runs distributed in production, it works identically because the infrastructure abstractions are the same.

The monoservice is not a compromise between monolith and microservices. It is both, simultaneously, selected by configuration.

---

## The Orchestrator's Role

The Orchestrator service (L3) is the intelligence that manages the transition from "one process" to "distributed services." It:

1. **Defines topologies** via deployment presets -- named YAML configurations that specify which services run on which nodes
2. **Deploys containers** using the pluggable backend (Compose, Swarm, Portainer, Kubernetes) with the correct service environment variables
3. **Broadcasts routing tables** (`bannou.full-service-mappings`) so lib-mesh knows where each service lives
4. **Manages processing pools** for auto-scaled workloads like NPC brain execution
5. **Monitors health** via per-service heartbeats and manages degradation

In development, the Orchestrator is just another service running in the same process. In production, it is the control plane that manages the distributed deployment. Same code, different role, selected by environment.

---

## The Practical Upshot

A game studio evaluating Bannou starts with `BANNOU_SERVICES_ENABLED=true` and a single Docker Compose file. Everything works. As the game grows:

- They move auth to dedicated nodes for security isolation -- change environment variables
- They scale NPC processing to a worker pool -- configure the Orchestrator
- They put voice on geographically distributed nodes -- deploy more containers
- They keep game state on high-memory nodes -- adjust the topology preset

At no point do they rewrite code, change service interfaces, or modify business logic. The same binary that ran on a laptop runs on a hundred nodes. The deployment model is a configuration concern, not a code concern.

This is what "monoservice" means. Not "monolith with a fancy name." A single codebase that genuinely deploys at any scale, with the same code, the same debugging tools, and the same operational model.
