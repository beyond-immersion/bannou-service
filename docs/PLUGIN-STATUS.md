# Plugin Production Readiness Status

> **Last Updated**: 2026-02-13
> **Scope**: All Bannou service plugins

---

## What This Document Is

A quick-reference scorecard for every plugin's production readiness. Each entry contains:

- **Production Readiness Score** (0-100%): How close the plugin is to production-ready. 95% is the realistic ceiling (there's always something). 0% means schema-only stub with no implementation.
- **Bug Count**: Number of known bugs from the deep dive's "Bugs (Fix Immediately)" section.
- **Top 3 Bugs**: The most critical bugs, with descriptions and issue links.
- **Top 3 Enhancements**: The most impactful unimplemented features/extensions, with descriptions and issue links.
- **GH Issues Link**: A `gh` command to see all open issues for that plugin.

## What This Document Is NOT

This is **NOT** a code investigation tool. It reports the state depicted in each plugin's [deep dive document](plugins/) and the GitHub issue tracker -- nothing more. It does not discover new bugs, audit tenet compliance, or evaluate code quality. If you're looking for that, use `/audit-plugin`.

## How To Update This Document

1. Read the plugin's deep dive document in `docs/plugins/{PLUGIN}.md`
2. Check the **Bugs (Fix Immediately)** section for the bug count and top 3 (or bugs masquerading in other sections if empty)
3. Check the **Stubs & Unimplemented Features**, **Potential Extensions**, and **Design Considerations** sections for the top 3 enhancements
4. Run `make print-models PLUGIN="{service}"` if you need to verify model completeness
5. Glance at the configuration schema (`schemas/{service}-configuration.yaml`) if you need to verify config coverage
6. Update the score, bugs, and enhancements below
7. **If ONE source code file is read, the update task has failed and must start over.** Deep dives and schemas only.

## Sources

- **Deep Dives**: `docs/plugins/{PLUGIN}.md` -- the authoritative source for known bugs, quirks, stubs, and extensions
- **GitHub Issues**: `gh issue list --search "{Plugin}:" --state open` -- tracked work items
- **Configuration Schemas**: `schemas/{service}-configuration.yaml` -- config property coverage
- **Model Shapes**: `make print-models PLUGIN="{service}"` -- request/response model completeness

---

## Status Table

| Plugin | Layer | Score | Bugs | Summary |
|--------|-------|-------|------|---------|
| [State](#state-status) | L0 | 95% | 0 | Rock-solid foundation. No stubs, no bugs. Only migration tooling remains. |
| [Messaging](#messaging-status) | L0 | 82% | 0 | Core pub/sub excellent. Stubs (lifecycle events, metrics) and design debt remain. |
| [Mesh](#mesh-status) | L0 | 93% | 0 | Feature-complete. Circuit breaker, health checks, load balancing all done. |
| [Account](#account-status) | L1 | 92% | 0 | Production-ready. Only post-launch extensions remain. |
| [Auth](#auth-status) | L1 | 88% | 0 | Core complete with MFA. Remaining items are downstream integration. |
| [Actor](#actor-status) | L2 | 65% | 0 | Solid core architecture. Auto-scale stubbed, many production features TODO. |
| [Orchestrator](#orchestrator-status) | L3 | 58% | 0 | Compose backend works. 3/4 backends stubbed. Pool auto-scale/idle timeout missing. |

---

## State {#state-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [STATE.md](plugins/STATE.md)

### Production Readiness: 95%

The bedrock of the entire platform. Provides unified Redis/MySQL/InMemory state access to all 54 services via `IStateStoreFactory`. Manages ~107 state stores (~70 Redis, ~37 MySQL). Full interface hierarchy: `IStateStore<T>` (core CRUD), `ICacheableStateStore<T>` (sets, sorted sets, counters, hashes), `ISearchableStateStore<T>` (full-text via RedisSearch), `IQueryableStateStore<T>` / `IJsonQueryableStateStore<T>` (MySQL LINQ/JSON path queries), `IRedisOperations` (Lua scripts), `IDistributedLockProvider` (distributed mutex). Optimistic concurrency via ETags on all backends. Error event publishing with deduplication. No stubs, no bugs, no design considerations. 11 well-documented intentional quirks. The only extension is store migration tooling.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Store migration tooling** | No mechanism to move data between Redis and MySQL backends without downtime. Needed for production scenarios where a store's backend needs to change (e.g., promoting ephemeral Redis data to durable MySQL). | [#190](https://github.com/beyond-immersion/bannou-service/issues/190) |
| 2 | *(No further enhancements identified)* | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "State:" --state open
```

---

## Messaging {#messaging-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [MESSAGING.md](plugins/MESSAGING.md)

### Production Readiness: 82%

Core pub/sub infrastructure is robust: RabbitMQ channel pooling (100 default, 1000 max), publisher confirms for at-least-once delivery, aggressive retry buffer with crash-fast philosophy (500k message / 10 minute threshold), backpressure at 80% buffer fill, dead-letter exchange with configurable limits, poison message handling with retry counting, HTTP callback subscriptions with recovery, event consumer fan-out bridge (`NativeEventConsumerBackend`), in-memory mode for testing. 30+ configuration properties all wired. No bugs.

However, 3 stubs remain (lifecycle events never implemented, `ListTopics` message count always 0, no Prometheus metrics), and 5 design considerations need attention (in-memory mode limitations, no graceful drain on shutdown, ServiceId from global static, tap exchange auto-creation, publisher confirms latency tradeoff).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Prometheus metrics** | Publish/subscribe rates, retry buffer depth, channel pool utilization, and retry counts are not exposed as metrics. Critical for operating the messaging infrastructure at scale (100k+ NPC agents generating events). Listed as both a stub and an extension. | No issue |
| 2 | **Dead-letter processing consumer** | No background service to process the DLX queue. Poison messages land in dead-letter and sit there. Need: alerting, logging, optional reprocessing for transient failures that exceeded retry attempts. | No issue |
| 3 | **No graceful drain on shutdown** | `DisposeAsync` iterates subscriptions without timeout. A hung subscription disposal could hang the entire shutdown process. Needs a timeout-bounded drain with forced cleanup. | No issue |

### GH Issues

```bash
gh issue list --search "Messaging:" --state open
```

---

## Mesh {#mesh-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [MESH.md](plugins/MESH.md)

### Production Readiness: 93%

Feature-complete service mesh: YARP-based HTTP routing, Redis-backed service discovery with TTL health tracking, 5 load balancing algorithms (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random), distributed per-appId circuit breaker with Lua-backed atomic state transitions and cross-instance sync via RabbitMQ, retry with exponential backoff, proactive health checking with automatic deregistration, degradation detection, event-driven auto-registration from Orchestrator heartbeats, endpoint caching. 27 configuration properties all wired. No stubs, no bugs, no design considerations. All production readiness issues closed. Only two speculative extensions remain.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Graceful draining** | Endpoint status `ShuttingDown` could actively drain connections before full deregistration. Currently deregistration is immediate -- in-flight requests to that endpoint may fail. | No issue |
| 2 | *(No further enhancements identified)* | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Mesh:" --state open
```

---

## Account {#account-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [ACCOUNT.md](plugins/ACCOUNT.md)

### Production Readiness: 92%

All CRUD operations complete with optimistic concurrency. OAuth/Steam account support with nullable email. Server-side paginated listing via MySQL JSON queries. Email change with distributed locking. Bulk operations (batch-get, count, bulk role update). Production hardening pass completed (distributed locks, ETag concurrency, stale index detection). No stubs, no bugs, no design considerations remaining. The only open items are post-launch extensions (account merge, audit trail).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Account merge workflow** | No mechanism to merge two accounts (e.g., email registration + OAuth under different email). Data model supports multiple auth methods, but no merge orchestration exists. Complex cross-service operation (40+ services reference accounts). Post-launch compliance feature. | [#137](https://github.com/beyond-immersion/bannou-service/issues/137) |
| 2 | **Per-account audit trail** | Account mutations publish events but don't maintain a per-account change history. Deep dive notes zero Account-side code changes needed -- this is purely a consumer-side feature (likely Analytics L4 or dedicated audit service). | #138 (closed) |
| 3 | *(No further enhancements identified)* | | |

### GH Issues

```bash
gh issue list --search "Account:" --state open
```

---

## Auth {#auth-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [AUTH.md](plugins/AUTH.md)

### Production Readiness: 88%

Full authentication suite: email/password, OAuth (Discord/Google/Twitch), Steam tickets, JWT tokens, session management, password reset, login rate limiting, TOTP-based MFA with recovery codes, edge token revocation (CloudFlare/OpenResty). 46 configuration properties covering all auth flows. All session lifecycle events published. No bugs. Remaining work is downstream integration: audit event consumers (Analytics), email change propagation (when Account adds it), and account merge session handling.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Audit event consumers** | Auth publishes 6 audit event types (login success/fail, registration, OAuth, Steam, password reset) but no service subscribes to them. Per-email rate limiting already exists via Redis counters -- the gap is Analytics (L4) consuming events for IP-level cross-account correlation, anomaly detection, and admin alerting. | [#142](https://github.com/beyond-immersion/bannou-service/issues/142) |
| 2 | **Email change propagation** | When Account adds email change, Auth must propagate new email to active sessions. Handler exists (`HandleAccountUpdatedAsync`) but only watches for `"roles"` changes, not `"email"`. Needs: distributed lock per account, security notification to old email, `session.updated` event. Deep dive marks this as READY. | Auth-side of [#139](https://github.com/beyond-immersion/bannou-service/issues/139) (Account-side closed) |
| 3 | **Account merge session handling** | When account merge is implemented, Auth needs to handle a new `account.merged` event: invalidate all sessions for the source account and optionally refresh target account sessions with merged roles. Auth's handler is straightforward; the merge itself is Account-layer orchestration. Low priority -- post-launch. | Auth-side of [#137](https://github.com/beyond-immersion/bannou-service/issues/137) |

### GH Issues

```bash
gh issue list --search "Auth:" --state open
```

---

## Actor {#actor-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [ACTOR.md](plugins/ACTOR.md)

### Production Readiness: 65%

Core architecture is solid and functional: template CRUD, actor spawn/stop/get lifecycle, behavior loop with perception processing, ABML document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, encounter management, pool mode with command topics and health monitoring, Variable Provider Factory pattern (personality/combat/backstory/encounters/quest), behavior document provider chain (Puppetmaster dynamic + seeded + fallback), periodic state persistence, character state update publishing. 30+ configuration properties all wired. No bugs, no implementation gaps.

However, significant production features remain unimplemented: auto-scale deployment mode is declared but stubbed, session-bound actors are stubbed, and 5 extensions (memory decay, cross-node encounters, behavior versioning, actor migration, Phase 2 variable providers) are open. The pool node capacity model is self-reported with no external validation.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Auto-scale deployment mode** | Declared as a valid `DeploymentMode` enum value but no auto-scaling logic is implemented. Pool nodes must be manually managed or pre-provisioned. This is essential for the 100,000+ concurrent NPC target -- manual pool sizing won't work at scale. | [#318](https://github.com/beyond-immersion/bannou-service/issues/318) |
| 2 | **Session-bound actors** | `HandleSessionDisconnectedAsync` is stubbed. Currently all actors are NPC brains that continue running when players disconnect. Future need: actors tied to player sessions that stop on disconnect (e.g., player-controlled summons, temporary companions). | [#191](https://github.com/beyond-immersion/bannou-service/issues/191) |
| 3 | **Actor migration** | No mechanism to move running actors between pool nodes without state loss. Required for production load balancing -- without it, hot nodes stay hot until actors naturally stop. Needs: state snapshot transfer, perception queue draining, subscription re-establishment on target node. | [#393](https://github.com/beyond-immersion/bannou-service/issues/393) |

### GH Issues

```bash
gh issue list --search "Actor:" --state open
```

---

## Orchestrator {#orchestrator-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

### Production Readiness: 58%

The Docker Compose backend is functional and solid: preset-based deployment, live topology updates (add/remove/move/scale/update-env), service-to-app-id routing broadcasts consumed by Mesh, processing pool acquire/release with distributed locks, config versioning with rollback, health monitoring with source-filtered reports (control plane vs deployed vs all), container management (restart, status, logs), and infrastructure health checks. 25 configuration properties all wired. No bugs.

However, 3 of 4 container backends are stubs (Swarm, Kubernetes, Portainer -- only Compose is implemented). Processing pool management is missing auto-scaling (thresholds stored but no trigger), idle timeout enforcement (config stored but no timer), lease expiry enforcement (lazy reclamation only on next acquire), and queue depth tracking (hardcoded 0). Design consideration #4 notes the scoped service TTL cache is structurally ineffective. Design consideration #5 notes `_lastKnownDeployment` is in-memory state that would diverge across instances.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Processing pool auto-scaling** | Scale-up/down thresholds (`ScaleUpThreshold`, `ScaleDownThreshold`) are stored in pool config but no background service evaluates them. Pools cannot auto-scale -- only manual `ScalePool` API calls work. Critical for the 100k+ NPC target where actor pool demand is dynamic. | [#252](https://github.com/beyond-immersion/bannou-service/issues/252) (queue design) / [#318](https://github.com/beyond-immersion/bannou-service/issues/318) (actor auto-scale) |
| 2 | **Kubernetes/Swarm/Portainer backends** | Only Docker Compose is implemented. Swarm, Kubernetes, and Portainer backends are stubs (interface methods return NotImplemented or minimal responses). Required for any production deployment beyond single-machine dev. | No issue |
| 3 | **Lease expiry enforcement** | Expired processor leases are only reclaimed lazily during the next `AcquireProcessorAsync` call. No background timer proactively scans for expired leases. Pools with no acquire traffic will hold expired leases indefinitely, wasting worker containers. | No issue |

### GH Issues

```bash
gh issue list --search "Orchestrator:" --state open
```

---

*This document reports plugin status from deep dive documents and the GitHub issue tracker. For code-level auditing, use `/audit-plugin`.*
