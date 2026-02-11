# Why Does Bannou Have an Orchestrator Service When Kubernetes Already Exists?

> **Short Answer**: Kubernetes orchestrates containers. The Orchestrator orchestrates Bannou services. These are different problems at different abstraction levels, and Kubernetes is one of several backends the Orchestrator can use.

---

## The Superficial Overlap

At first glance, the Orchestrator service looks like it duplicates Kubernetes:
- It manages service deployments
- It monitors service health
- It scales worker pools
- It handles configuration and rollback

If you already have Kubernetes, why build another orchestration layer?

---

## What Kubernetes Does Not Know

Kubernetes knows about containers, pods, deployments, and services. It knows how to schedule workloads, restart crashed pods, and expose network endpoints. It is very good at this.

Kubernetes does not know:
- **Which Bannou services are enabled in which container**. A single Bannou binary can run any combination of 48 services. Kubernetes sees one deployment of one image. The Orchestrator knows that this container is running Account, Auth, and Connect while that container is running Character, Realm, and Species.
- **The service hierarchy**. Kubernetes does not know that L1 services must start before L2 services, or that disabling L2 means L4 should also be disabled. The Orchestrator understands deployment modes and layer dependencies.
- **Service-to-app-id routing**. When a Bannou service calls another service via lib-mesh, the mesh needs to know which container instance hosts the target service. The Orchestrator maintains and broadcasts service routing tables (`bannou.full-service-mappings`) that map service names to container app-ids. Kubernetes service discovery works at the container level; Bannou needs service-level routing within containers.
- **Processing pool semantics**. The Actor service needs on-demand worker containers for NPC brain execution. These are not standard Kubernetes workloads -- they are leased processing nodes acquired for a specific task, released when done, with metrics tracking and pool scaling. The Orchestrator provides acquire/release/metrics semantics that Kubernetes HPA does not model.
- **Preset-based topologies**. The Orchestrator supports deployment presets -- YAML files that declare which services run on which nodes in a named topology. "Deploy the http-tests topology" is a single API call that reconfigures the entire service distribution. Kubernetes has no concept of named, switchable deployment topologies for a single application.

---

## The Pluggable Backend Architecture

The Orchestrator does not replace Kubernetes. It uses it -- as one of several pluggable backends:

| Backend | Use Case |
|---------|----------|
| **Docker Compose** | Local development, CI/CD pipelines |
| **Docker Swarm** | Small-scale production, simple multi-node |
| **Portainer** | Web-managed Docker environments |
| **Kubernetes** | Large-scale production, cloud-native |

The `IContainerOrchestrator` interface abstracts container lifecycle operations (deploy, teardown, scale, restart, logs, status). Each backend implements this interface. The Orchestrator's business logic -- deployment presets, health monitoring, routing tables, processing pools -- works identically regardless of which backend runs the containers.

This means:
- A developer uses Docker Compose locally
- CI runs tests on Docker Compose
- A small studio deploys via Docker Swarm or Portainer
- A large deployment uses Kubernetes

The Orchestrator API is the same in every case. The service topology, routing tables, and processing pool management work identically. Only the container lifecycle calls differ.

---

## The Processing Pool Problem

The most concrete example of why the Orchestrator exists is **processing pools** for NPC brain execution.

Arcadia targets 100,000+ concurrent AI NPCs. Each NPC runs a long-lived cognitive loop on the Actor service. At scale, this requires dynamically spawning worker containers dedicated to actor execution -- containers that run only the Actor service and are scaled based on NPC load, not HTTP request load.

The workflow:
1. Actor service determines it needs more processing capacity
2. Actor service calls `Orchestrator.AcquireProcessorAsync(poolType: "actor")`
3. Orchestrator checks the pool, spins up a container if needed, returns a lease
4. Actor service uses the leased container for NPC execution
5. When done, Actor calls `Orchestrator.ReleaseProcessorAsync(leaseId)`
6. Orchestrator returns the container to the pool or tears it down based on pool configuration

This is a **domain-specific resource management pattern** -- leased processing nodes with pool metrics, minimum/maximum instance counts, and usage-based scaling. Kubernetes HPA scales based on CPU/memory metrics. The Orchestrator scales based on "how many NPC brains need containers right now."

You could model this in Kubernetes with custom resources, operators, and controllers. At that point, you have built the Orchestrator -- just in Go instead of C#, with Kubernetes-specific abstractions instead of portable ones, and without the ability to run the same logic on Docker Compose during development.

---

## Health Monitoring Beyond Liveness Probes

Kubernetes has liveness and readiness probes. These tell Kubernetes whether a container is running and ready to receive traffic. They do not tell you:

- Which of the 48 services inside the container are healthy
- Whether a specific service's state store is connected
- Whether the messaging infrastructure is operational for a specific service
- What the service's current capacity utilization is

The Orchestrator consumes `bannou.service-heartbeat` events that include per-service health status, capacity metrics, and issue lists. It maintains a real-time view of which services are healthy, degraded, or failed -- at the service level, not the container level.

A container might be healthy (Kubernetes liveness probe passes) while one of its services has lost its Redis connection and is silently failing. Kubernetes does not see this. The Orchestrator does, because the heartbeat system reports per-service health.

---

## The Deployment Intelligence Gap

Kubernetes is **declarative**: you tell it what you want, and it makes it happen. This is powerful for steady-state operations.

The Orchestrator is **intelligent**: it understands the relationships between services, the implications of topology changes, and the operational context of the platform. When you deploy a new topology:

1. The Orchestrator evaluates which services need to move
2. It determines the deployment order based on the service hierarchy
3. It deploys containers with the correct service configurations
4. It waits for heartbeats confirming services are healthy
5. It updates routing tables and broadcasts them to the mesh
6. It invalidates any OpenResty caches that reference moved services
7. It publishes deployment events for audit and monitoring

Steps 3 through 7 involve Bannou-specific domain knowledge that no generic orchestrator possesses. Kubernetes can run step 3 (deploy containers). The rest requires understanding what Bannou services are, how they route, and what needs to happen when the topology changes.

---

## Why Not a Kubernetes Operator?

A Kubernetes operator would be a reasonable alternative for Kubernetes-only deployments. The reasons the Orchestrator is a Bannou service instead:

1. **Backend portability**: The Orchestrator works with Docker Compose, Swarm, Portainer, and Kubernetes. A Kubernetes operator only works with Kubernetes. For a platform that promises "local development to production with the same binary," locking the orchestration layer to one backend defeats the purpose.

2. **Unified API**: The Orchestrator is a Bannou service with an OpenAPI schema, generated clients, and standard Bannou patterns. Other services call it via lib-mesh like any other service. A Kubernetes operator would require a separate API surface, separate client generation, and a separate communication pattern.

3. **State integration**: The Orchestrator uses lib-state (Redis) for its state management, the same as every other Bannou service. A Kubernetes operator would use etcd or custom resources, introducing a second state management pattern.

4. **Development experience**: During local development, the Orchestrator runs in the same process as every other service. You can set breakpoints, inspect state, and debug orchestration logic with the same tools you use for any other service. A Kubernetes operator requires a Kubernetes cluster even for development.

---

## The Summary

| Concern | Kubernetes | Orchestrator |
|---------|-----------|-------------|
| Container lifecycle | Manages pods and deployments | Delegates to Kubernetes (or Compose, Swarm, Portainer) |
| Service routing | Container-level service discovery | Service-level routing within containers |
| Health monitoring | Liveness/readiness probes | Per-service heartbeat with capacity metrics |
| Scaling | HPA based on CPU/memory | Processing pools based on domain-specific load |
| Topology | Declarative manifests | Named presets with hierarchy-aware deployment order |
| Scope | Any containerized workload | Bannou services specifically |

Kubernetes is the infrastructure. The Orchestrator is the intelligence that knows how to use that infrastructure for Bannou's specific needs. They are complementary, not redundant.
