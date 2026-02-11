# Why Does Bannou Precompile Permission Manifests Instead of Checking Permissions Per-Request?

> **Short Answer**: Because the system routes 100,000+ concurrent NPC decisions and player actions through the WebSocket gateway. Checking a multi-dimensional permission matrix on every message would add latency to every single operation. Precompiling the manifest once and pushing it to the client turns permission enforcement into a local lookup at the gateway -- zero additional latency per message.

---

## The Standard Approach

Most web applications check permissions per-request. A request arrives, middleware extracts the user's identity, queries their roles or permissions from a database or cache, evaluates whether those permissions allow the requested action, and either proceeds or returns 403. This works well when requests arrive at a rate of tens or hundreds per second per user.

Bannou's Permission service does something different. Instead of checking permissions on every inbound message, it **precompiles** a complete capability manifest for each connected session and pushes it to the client. The Connect gateway then enforces access by checking if the requested endpoint GUID exists in the session's manifest -- a simple set membership test.

---

## The Scale Problem

Consider what per-request permission checking would mean in Bannou's architecture:

The permission model is a **multi-dimensional matrix**: `service x state x role -> allowed endpoints`. A single session might have multiple roles (user, admin, developer), multiple states (authenticated, in_game, in_match, in_call), and access to endpoints across dozens of services. Evaluating this matrix means:

1. Look up the session's current roles.
2. Look up the session's current states (one per service -- in_game via GameSession, in_match via Matchmaking, in_call via Voice).
3. For each role-state combination, look up the allowed endpoints.
4. Union the results.
5. Check if the requested endpoint is in the union.

This is not a simple "does user have role X" check. It is a multi-key Redis lookup followed by set operations. At the message rates Bannou handles -- player actions, NPC state updates, real-time game events -- doing this on every message would add measurable latency.

With precompilation, this entire calculation happens **once** when the session state changes. The result is a flat set of allowed endpoint GUIDs stored in Redis and pushed to the Connect gateway. On every subsequent message, the gateway checks: "Is this GUID in the set?" That is an O(1) operation with no external calls.

---

## The Push Model

Precompilation enables a **push** model for permission changes. When something changes that affects a session's permissions, the Permission service recompiles and pushes the updated manifest immediately:

| Trigger | What Changes | Who Recompiles |
|---------|-------------|----------------|
| User authenticates | Roles added (anonymous -> user) | Permission recompiles on `session.connected` |
| Admin grants role | New role added to session | Permission recompiles on `session.updated` from Auth |
| Player joins game | `in_game` state set | Permission recompiles on `session-state-changed` from GameSession |
| Player enters match | `in_match` state set | Permission recompiles on `session-state-changed` from Matchmaking |
| Player starts voice call | `in_call` state set | Permission recompiles on `session-state-changed` from Voice |
| New service deploys | New endpoints registered | Permission recompiles on `service-registered` |

Each of these triggers exactly one recompilation for the affected session(s). Between triggers, every message for that session uses the precompiled manifest with zero permission overhead.

In a per-request model, every one of these state dimensions would need to be queried on every request. Even with caching, cache invalidation on any state change requires the same fan-out logic that the precompilation model already implements -- except now it also handles the happy path (no state change) by doing unnecessary work.

---

## The Client-Side Benefit

The precompiled manifest is not just a server-side optimization. It is pushed to the client. This means the client knows exactly which endpoints are available to it at any moment:

- The game client can **dynamically show or hide UI elements** based on available capabilities. If the player does not have the `in_game` state, game-specific UI elements are hidden -- not because the client hard-codes which roles see which UI, but because the manifest literally does not contain those endpoint GUIDs.
- The client can **prevent invalid requests** before sending them. If an endpoint GUID is not in the manifest, the client knows not to call it. This eliminates round-trips for requests that would be rejected anyway.
- **Permission changes are visible immediately.** When an admin grants a new role, the client's manifest updates in real time via the WebSocket connection. No page refresh, no re-authentication, no polling.

Per-request checking provides none of this. The client has no way to know what it is allowed to do without trying each endpoint and seeing if it gets a 403.

---

## The Registration Model

Services declare their permission requirements in their OpenAPI schema using the `x-permissions` extension:

```yaml
/account/get:
  post:
    x-permissions:
      - state: authenticated
        roles: [user, admin]
      - state: in_game
        roles: [user]
```

On startup, each service publishes its permission matrix via a `ServiceRegistrationEvent`. The Permission service receives these registrations, builds the matrix in Redis, and hashes the registration data for idempotent change detection. If a service restarts and publishes the same matrix, Permission detects no change and skips recompilation.

This means adding permissions to a new service requires zero changes to the Permission service itself. The service declares what it needs in its schema, the code generator produces the registration event, and Permission learns about it dynamically. The Permission service does not contain a list of services or endpoints -- it discovers them at runtime.

---

## The Cost of Precompilation

The honest trade-off: precompilation means permission changes have slightly higher latency than per-request checking. When a role changes, there is a brief window (typically milliseconds) between the event being published and the new manifest being pushed. During this window, the session operates with stale permissions.

In practice, this is not a meaningful concern:

- Permission changes are infrequent (role grants, state transitions) compared to message volume.
- The staleness window is bounded by event delivery latency (RabbitMQ) plus recompilation time (Redis operations).
- The alternative -- per-request checking with zero staleness -- adds latency to every single message instead of adding latency to the rare permission change event.

The math is clear: pay a small cost on rare events, or pay a small cost on every message. At Bannou's message volume, the precompilation model wins by orders of magnitude.

---

## Why Not Cache Per-Request Results?

The natural objection is: "Just cache the per-request permission check result." This is what most systems do -- check once, cache for N seconds, invalidate on change.

The problem is that this reinvents precompilation with worse semantics:

- You still need the cache invalidation logic (the same event-driven recompilation triggers).
- You still need to push the invalidation to the gateway (the same fan-out to affected sessions).
- But now you also have TTL-based staleness in the happy path (cached result expires even though nothing changed).
- And you have the cold-cache penalty (first request after invalidation pays full computation cost).

Precompilation is the logical endpoint of "per-request checking with aggressive caching and proactive invalidation." Bannou just skips the intermediate steps and goes directly to the endpoint.
