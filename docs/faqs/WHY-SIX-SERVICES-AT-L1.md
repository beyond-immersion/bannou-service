# Why Are There Six Whole Services at L1 Instead of Fewer, Larger Ones?

> **Short Answer**: Because each L1 service has a different trust boundary, different state backend, different scaling profile, and different failure blast radius. Merging any two of them creates a service that is harder to reason about, harder to scale, and more dangerous when it fails.

---

## The Six L1 Services

| Service | Internet-Facing | State Backend | Singleton | Primary Role |
|---------|----------------|---------------|-----------|-------------|
| **Account** | No | MySQL | No | User record CRUD |
| **Auth** | Yes | Redis | No | Authentication, sessions, JWT |
| **Connect** | Yes | Redis | Yes | WebSocket gateway, binary routing |
| **Permission** | No | Redis | No | RBAC manifests, capability compilation |
| **Contract** | No | Redis | No | FSM, consent flows, milestone tracking |
| **Resource** | No | Redis + MySQL | No | Reference counting, cleanup, compression |

The instinct is to merge some of these. "Auth and Account are both about users -- merge them." "Permission is just part of Auth." "Contract and Resource are both coordination services -- merge them."

Each of these merges creates specific, identifiable problems.

---

## Why Not Merge Auth + Account?

This is covered in detail in [WHY-SEPARATE-AUTH-FROM-ACCOUNT.md](WHY-SEPARATE-AUTH-FROM-ACCOUNT.md). The short version: Auth is internet-facing with ephemeral Redis state and bursty traffic. Account is internal-only with durable MySQL state and steady traffic. Merging them puts your user data store on the same attack surface as your login endpoint.

---

## Why Not Merge Auth + Permission?

Auth handles **identity**: who are you? Permission handles **authorization**: what can you do?

These change at different rates and for different reasons:

- Auth changes when you add a new OAuth provider, change JWT expiration policy, or add MFA support. These are security-boundary changes.
- Permission changes when any service adds new endpoints, when the state machine adds new states (in_game, in_match, in_call), or when the RBAC matrix is restructured. These are capability-boundary changes.

Permission also has a unique relationship with Connect: it pushes compiled capability manifests to per-session RabbitMQ queues. This push-model interaction is specific to Permission and has nothing to do with authentication. In a merged service, the JWT validation path and the capability push path would share failure modes -- a bug in capability compilation could affect token validation, and vice versa.

---

## Why Not Merge Contract + Resource?

Contract manages **agreements between parties** with FSM-based lifecycle, consent gathering, and prebound API execution. Resource manages **reference counting between entities** with cleanup coordination and hierarchical compression.

These serve completely different purposes. The overlap is superficial -- both "coordinate" things. But:

- Contract tracks state machines with parties, milestones, terms, and consent. Its data model is complex and domain-specific to agreement management.
- Resource tracks reference counts with source types, grace periods, and cleanup callbacks. Its data model is simple and purpose-built for lifecycle management.

Contract is consumed by Quest (L2), Escrow (L4), and License (L4) for agreement workflows. Resource is consumed by Character (L2) for deletion safety and by L4 services for reference publishing and compression. The consumer sets barely overlap.

Merging them would create a service that is simultaneously a generic FSM engine and a reference counting system. Developers working on quest milestone logic would share a codebase with developers working on character deletion safety. A bug in consent flow handling could affect reference counting. The conceptual boundary between "agreements" and "references" would dissolve in the implementation even though they are fundamentally different abstractions.

---

## Why Not Collapse Everything into "App Foundation Service"?

This is the maximalist merge: one L1 service that handles accounts, authentication, WebSocket gateway, permissions, contracts, and resource lifecycle. It would have approximately 99 endpoints (18 + 19 + 5 + 8 + 30 + 17) and manage MySQL, Redis, and in-memory state simultaneously.

The problems compound:

**1. Singleton conflict.** Connect is registered as a Singleton because it maintains in-memory WebSocket connection state. Every other L1 service is Scoped (one instance per request). A merged service would need to be Singleton (for connection state) while also handling scoped database connections (for account CRUD). This is architecturally incoherent in ASP.NET's DI model.

**2. Mixed trust boundaries.** Auth and Connect are internet-facing. Account, Permission, Contract, and Resource are internal-only. A merged service is internet-facing by definition (the most permissive boundary wins). Every internal-only endpoint is now accessible to the internet and must be independently secured.

**3. Blast radius.** If Connect crashes, WebSocket connections drop but HTTP API calls continue. If Auth crashes, new logins fail but existing sessions continue. If Permission crashes, capability manifests go stale but existing permissions continue working. Each service's failure is contained to its domain. A merged service's crash takes down authentication, connections, permissions, contracts, and resource management simultaneously.

**4. Deployment inflexibility.** In production, you might want to scale Connect horizontally (more WebSocket connections) without scaling Account (low traffic). You might want to put Auth behind a WAF with aggressive rate limiting without applying the same restrictions to internal-only Resource. With six services, these are independent operational decisions. With one service, they are impossible.

---

## The Design Principle

The L1 services solve six distinct concerns at the application foundation layer, each with:

- A clear, single responsibility
- A distinct trust boundary (internet-facing vs. internal-only)
- A distinct state management strategy (MySQL vs. Redis vs. both)
- A distinct scaling profile (bursty vs. steady, high-throughput vs. low-frequency)
- A distinct failure blast radius (connection loss vs. auth failure vs. stale permissions)
- A distinct set of consumers (Auth consumed by Connect; Permission consumed by all services; Contract consumed by Quest/Escrow/License; Resource consumed by Character/L4 services)

Merging any two of them saves zero operational complexity (they are all deployed as plugins in the same binary during development) while losing the ability to reason about, scale, and operate them independently in production. The "cost" of six services is six schema files and six plugin directories. The benefit is complete operational independence between six fundamentally different concerns.

---

## The Monoservice Advantage

This is where Bannou's monoservice architecture resolves the tension. In a traditional microservices world, six services means six repositories, six CI pipelines, six deployment targets, six monitoring dashboards. The operational overhead is real.

In Bannou, six L1 services means six plugins in one binary. In development, they all run in one process. In production, they can be split across nodes based on load characteristics. The "cost" of additional services is near-zero in development and provides maximum flexibility in production.

The question is not "can we get away with fewer services?" The question is "do these concerns genuinely need to be independent?" For L1, the answer is yes for every pair.
