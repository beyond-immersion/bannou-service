# Why Is Authentication Separated from Account Management?

> **Short Answer**: Because the two services have fundamentally different trust boundaries, scaling characteristics, and failure modes. Merging them creates a single service that is simultaneously internet-facing and the authoritative data store -- a security and operational anti-pattern.

---

## The Intuitive Objection

Most web frameworks combine authentication and account management into one "Users" module. Rails has Devise. Django has `django.contrib.auth`. Express apps throw Passport.js and a User model into the same codebase. When you see Bannou split this into two separate L1 services -- Auth (19 endpoints, version 4.0.0) and Account (18 endpoints, version 2.0.0) -- the natural question is: why complicate things?

The answer has nothing to do with complexity for its own sake and everything to do with what happens when these concerns share a boundary.

---

## The Trust Boundary Problem

Auth is **internet-facing**. It accepts login credentials from the open internet, validates OAuth tokens against third-party providers, handles password reset flows, and issues JWTs. It is Bannou's primary attack surface alongside the Connect WebSocket gateway. Every request to Auth is potentially hostile.

Account is **internal-only**. It is never exposed to the internet. It performs CRUD operations on user records -- create, read, update, soft-delete. Every request to Account comes from another trusted service via lib-mesh, not from a user's browser.

When you merge these into one service, you get a single codebase that must simultaneously:

1. Accept untrusted input from the internet (login attempts, OAuth callbacks, password reset requests)
2. Serve as the authoritative data store for every user record in the system
3. Make security-critical decisions (rate limiting, lockouts, token generation) using the same data access patterns that serve routine CRUD

This means a vulnerability in the login flow -- a serialization bug, an injection, a bypass -- gives the attacker direct access to the account data store. There is no intermediate boundary to cross. The thing that receives hostile input IS the thing that stores the data.

With the split:

- A vulnerability in Auth gives the attacker access to Redis-backed ephemeral session data (TTL-limited, easily invalidated).
- Account data lives in a separate MySQL-backed store behind a separate service boundary. The attacker would need to compromise Auth AND then successfully impersonate an internal mesh call to reach Account.

This is defense in depth, not over-engineering.

---

## The Scaling Problem

Auth and Account have radically different load profiles:

**Auth** experiences:

- Bursty login traffic (game launch, marketing events, server restarts)
- High-frequency token validation (every WebSocket connection establishment)
- OAuth callback spikes (when a streamer says "link your Discord account")
- Failed login attempt storms (credential stuffing, brute force)
- Session refresh traffic (every JWT expiration cycle)

**Account** experiences:

- Low-frequency CRUD (account creation happens once per user, updates are rare)
- Infrequent lookups (by-email during login, by-ID during token refresh)
- Occasional admin operations (list accounts, bulk operations)
- Near-zero write volume compared to Auth's session churn

Auth uses Redis for all its state because session data is ephemeral, needs sub-millisecond access, and has natural TTLs. Account uses MySQL because user records are durable, queryable, and need transactional consistency.

In a merged service, you cannot scale the login/validation path independently from the CRUD path. You cannot put Auth behind a separate load balancer profile. You cannot optimize Auth's Redis connection pool without affecting Account's MySQL connection pool. Every operational decision becomes a compromise between two fundamentally different workloads.

---

## The Dependency Direction

The split also clarifies who depends on whom. Auth depends on Account (it calls `IAccountClient` for lookups and creates). Account depends on nothing -- it is a leaf node in the service graph.

This means:

- **Account can be tested in complete isolation.** No auth flows, no OAuth mocking, no JWT concerns.
- **Auth can be updated without touching account storage.** Adding a new OAuth provider (Twitch, Steam) changes Auth's code and schema. Account is unaffected.
- **Account's API is the canonical CRUD interface.** Any future service that needs account data goes through the same clean interface that Auth uses. There is no "internal-only backdoor" that bypasses auth logic -- the auth logic lives in a different service entirely.

If they were merged, adding Steam authentication would mean modifying the same codebase that stores user passwords. The blast radius of every change encompasses both security-critical auth logic and data-critical storage logic.

---

## The Event Isolation

When an account is deleted, Auth needs to know so it can invalidate all sessions. When a session is invalidated, Connect needs to know so it can close WebSocket connections. This is a clean event chain:

```
Account publishes: account.deleted
    -> Auth subscribes: invalidates all sessions for that account
        -> Auth publishes: session.invalidated
            -> Connect subscribes: closes WebSocket connections
```

Each service reacts to events within its domain. Account handles data deletion. Auth handles session cleanup. Connect handles connection cleanup. No service reaches into another service's domain.

In a merged service, "delete account" becomes a method that must handle data deletion, session invalidation, token revocation, OAuth link cleanup, and password reset token cleanup in one transaction. The blast radius of a bug in any of these steps affects all of them.

---

## The Real-World Validation

Auth is at version 4.0.0. Account is at version 2.0.0. Auth has been rewritten or significantly restructured four times -- adding OAuth providers, adding MFA, adding edge token revocation, restructuring session management. Account has been stable, changing primarily to add email optionality and auth method management.

If they were the same service, every Auth restructuring would have touched Account's data model, migration path, and test suite. The split means Auth's rapid evolution and Account's stability don't interfere with each other.

---

## The Pattern

This is the same pattern used by Auth0, Okta, Firebase Auth, and every mature identity platform: the authentication gateway is a separate concern from the identity store. Bannou doesn't do this because those companies do it. Bannou does it because the reasons those companies do it -- trust boundaries, scaling characteristics, failure isolation, and independent evolution -- all apply with equal force to a game backend that needs to handle 100,000+ concurrent connections.
