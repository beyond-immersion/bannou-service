# Endpoint Permission Guidelines

> **Version**: 1.1
> **Last Updated**: 2026-03-06
> **Scope**: All Bannou service endpoints — how to choose x-permissions for every endpoint
> **Prerequisites**: [SCHEMA-RULES.md](SCHEMA-RULES.md) (x-permissions syntax), [FOUNDATION.md](tenets/FOUNDATION.md) (T13, T15, T32), [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md) (layer rules)

This document defines **when and why** each permission level is used. SCHEMA-RULES.md defines the **syntax**; this document defines the **semantics**. T13 says "all endpoints MUST declare x-permissions" — this document tells you which value to declare and why.

---

## The Seven Permission Levels

Every Bannou endpoint falls into exactly one of these seven categories. There are no hybrid cases.

| Level | x-permissions | WebSocket Access | Intended Caller |
|-------|---------------|------------------|-----------------|
| **Pre-Auth Public** | `[{role: anonymous}]` | All connected clients | Unauthenticated players |
| **Authenticated User** | `[{role: user}]` | Any authenticated session | Logged-in players, anytime |
| **State-Gated User** | `[{role: user, states: {...}}]` | Sessions with matching state | Players in a specific context |
| **Developer** | `[{role: developer}]` | Developer and admin sessions | Content authors, live ops team |
| **Admin** | `[{role: admin}]` | Admin sessions only | System administrators |
| **Service-to-Service** | `[]` | None | Other Bannou services via lib-mesh |
| **Browser-Facing** | *(T15 exception)* | N/A (NGINX-routed) | Web browsers directly |

The **role hierarchy** is cumulative: `admin` includes `developer`, which includes `user`, which includes `anonymous`. An admin session can call any endpoint that a user session can call.

---

## Level 1: Pre-Auth Public

```yaml
x-permissions:
  - role: anonymous
```

**What it means**: Any connected WebSocket client can call this endpoint, even before authenticating. The client has a GUID for this endpoint from the moment the WebSocket connection is established.

**When to use**: Only for endpoints that MUST be callable before the client has a JWT token. This is the authentication bootstrapping problem — you need to call `login` before you have credentials, so `login` must be anonymous.

**Established uses** (exhaustive — additions require justification):

| Service | Endpoint | Why Anonymous |
|---------|----------|---------------|
| Auth | `/auth/login` | Email/password login (creates the JWT) |
| Auth | `/auth/register` | Account creation (no account exists yet) |
| Auth | `/auth/steam/login` | Steam ticket validation (creates the JWT) |
| Auth | `/auth/password-reset/request` | Password reset (user may not be logged in) |
| Auth | `/auth/password-reset/verify` | Password reset verification |
| Auth | `/auth/mfa/verify` | MFA challenge during login flow |

**Rules**:
1. Only the Auth service should have anonymous endpoints under normal circumstances
2. Anonymous endpoints MUST NOT accept sensitive identifiers (accountId, sessionId) — the caller has no verified identity
3. Anonymous endpoints MUST implement aggressive rate limiting — they are the primary attack surface
4. If you think you need an anonymous endpoint outside Auth, you are almost certainly solving the wrong problem — present the use case for discussion

**Anti-pattern**: Making a read-only endpoint anonymous "because it doesn't modify anything." Read access still reveals information. Public data should go through the Website service (L3, browser-facing) or be pushed via client events after authentication.

---

## Level 2: Authenticated User

```yaml
x-permissions:
  - role: user
```

**What it means**: Any authenticated WebSocket session can call this endpoint at any time, regardless of what the player is currently doing (in a lobby, in a game, in the void, idle).

**When to use**: For operations that are always valid for an authenticated player, with no contextual prerequisites. The endpoint handles its own business-logic validation (e.g., "you don't have a garden" returns 404), but the Permission service does not gate access based on session state.

**Examples**:

| Service | Endpoint | Why Generic User |
|---------|----------|-----------------|
| Matchmaking | `/matchmaking/join` | Any authenticated player can join a queue |
| Matchmaking | `/matchmaking/queue/list` | Any authenticated player can browse queues |
| Matchmaking | `/matchmaking/stats` | Queue statistics are non-sensitive |
| Gardener | `/gardener/garden/enter` | Any player can enter the void/discovery experience |
| Gardener | `/gardener/scenario/enter` | Any player can enter a scenario (creates the context) |
| Account | `/account/profile/update` | Any authenticated user can update their own display name |

**Rules**:
1. The endpoint MUST NOT require the player to be in a specific game state — if it does, use State-Gated User instead
2. The endpoint MUST NOT accept `accountId` in the request body (T32) — resolve identity from the WebSocket session server-side
3. The endpoint SHOULD handle "wrong context" gracefully via business logic (return 404/409), not by being invisible to the client
4. Rate limiting SHOULD be applied to high-frequency endpoints (position updates, list queries)

**The "always available" test**: If a player opens their game menu while idle in a lobby and triggers this endpoint, is that a valid use case? If yes, it's Authenticated User. If it only makes sense mid-match or mid-queue, it's State-Gated.

---

## Level 3: State-Gated User

```yaml
x-permissions:
  - role: user
    states:
      matchmaking: in_queue      # Service-specific state key and value
```

**What it means**: The endpoint is only available to authenticated sessions that are currently in a specific state. The Permission service dynamically adds/removes the endpoint from the session's capability manifest as states change. The client literally cannot call the endpoint when the state is not active — it has no GUID for it.

**When to use**: For operations that only make sense within a specific context, where calling the endpoint outside that context would always be an error. State-gating prevents the error at the protocol level rather than the business-logic level.

**Examples**:

| Service | Endpoint | State Required | Why State-Gated |
|---------|----------|----------------|-----------------|
| Matchmaking | `/matchmaking/leave` | `matchmaking: in_queue` | Can't leave a queue you're not in |
| Matchmaking | `/matchmaking/status` | `matchmaking: in_queue` | Status is meaningless outside a queue |
| Matchmaking | `/matchmaking/accept` | `matchmaking: match_pending` | Can only accept when a match is formed |
| Matchmaking | `/matchmaking/decline` | `matchmaking: match_pending` | Can only decline when a match is formed |
| GameSession | `/sessions/leave` | `game-session: in_game` | Can't leave a session you're not in |

**Rules**:
1. The service that sets the state MUST also clear it when the context ends — stale states prevent access to other endpoints
2. State keys are namespaced per service (`matchmaking:`, `game-session:`, etc.) to prevent collisions
3. State changes trigger Permission recompilation, which pushes updated capability manifests to the client via Connect — this happens automatically
4. An endpoint MUST NOT be state-gated on another service's state unless that service explicitly documents the state as a public contract
5. Use state-gating only when the endpoint is **always invalid** outside the state — if it's merely unusual but possible, use Authenticated User with business-logic validation instead

### The Entry/Context/Exit Pattern (MANDATORY)

Any service that manages a session-like lifecycle (queue membership, game session, garden, scenario) MUST use state-gating with this three-phase pattern:

1. **Entry endpoint**: `role: user` (no state gate) — creates the context, sets the state
2. **In-context endpoints**: `role: user` + `states: {service: state}` — only available while in context
3. **Exit endpoint**: `role: user` + `states: {service: state}` — ends the context, clears the state

```
# Matchmaking example:
join     [role: user]                         → sets matchmaking:in_queue
status   [role: user, matchmaking:in_queue]   → reads state
leave    [role: user, matchmaking:in_queue]   → clears matchmaking:in_queue

# Gardener garden example:
enter    [role: user]                         → sets gardener:in_garden
get      [role: user, gardener:in_garden]     → reads state
position [role: user, gardener:in_garden]     → updates position
leave    [role: user, gardener:in_garden]     → clears gardener:in_garden

# Gardener scenario example:
enter    [role: user]                         → sets gardener:in_scenario
get      [role: user, gardener:in_scenario]   → reads state
complete [role: user, gardener:in_scenario]   → clears gardener:in_scenario
abandon  [role: user, gardener:in_scenario]   → clears gardener:in_scenario
chain    [role: user, gardener:in_scenario]   → replaces scenario (state persists)
```

**Why this is mandatory, not optional**: State-gating controls the client's capability manifest. Without it, the client has GUIDs for "Leave Garden" and "Complete Scenario" at all times, even when neither context is active. This causes UI confusion (buttons that always return 404), unnecessary network traffic, and a larger attack surface. Business-logic validation (returning 404) is a fallback for correctness, not a substitute for state-gating.

---

## Level 4: Developer

```yaml
x-permissions:
  - role: developer
```

**What it means**: Only WebSocket sessions with the `developer` role (or higher — admin includes developer) can call this endpoint. Used for live operations tools, content authoring, and development-time administration.

**When to use**: For endpoints that manage game content definitions, orchestrate live events, or provide development/debugging capabilities. The developer role represents the game's content team — people who author templates, tune parameters, and run live events, but who are not system administrators.

**Examples**:

| Service | Endpoint | Why Developer |
|---------|----------|---------------|
| Gardener | `/gardener/template/create` | Scenario template authoring |
| Gardener | `/gardener/template/update` | Scenario template tuning |
| Gardener | `/gardener/template/deprecate` | Content lifecycle management |
| Gardener | `/gardener/phase/update` | Deployment phase advancement |
| Gardener | `/gardener/phase/get-metrics` | Operational monitoring |
| Director | `/director/actor/tap` | Observe actor perception stream |
| Director | `/director/actor/steer` | Inject perceptions into actors |
| Director | `/director/actor/drive` | Take control of an actor |
| Storyline | All endpoints | Narrative generation is developer-initiated |

**Rules**:
1. Developer endpoints manage **definitions and templates**, not instances — template CRUD, phase configuration, content seeding
2. Developer endpoints MUST NOT perform destructive system operations (bulk deletes, database drops, service restarts) — those are Admin
3. Read-only developer endpoints (get, list, metrics) are still `role: developer`, not `role: user` — players should not see internal content structure
4. Developer endpoints on definition entities follow the standard deprecation lifecycle (T31 Category A/B) — the developer role does not exempt from deprecation rules

**Developer vs Admin decision**: "Would a game designer need this?" → Developer. "Would a system operator need this?" → Admin. Template CRUD is developer. Queue capacity configuration is admin. Live event orchestration is developer. Bulk account role changes is admin.

---

## Level 5: Admin

```yaml
x-permissions:
  - role: admin
```

**What it means**: Only WebSocket sessions with the `admin` role can call this endpoint. The highest privilege level for WebSocket-accessible endpoints. Used for system-level administration that affects infrastructure, security, or has high blast radius.

**When to use**: For endpoints that perform system configuration, manage resources at scale, or have security implications beyond normal gameplay.

**Examples**:

| Service | Endpoint | Why Admin |
|---------|----------|-----------|
| Matchmaking | `/matchmaking/queue/create` | Queue infrastructure configuration |
| Matchmaking | `/matchmaking/queue/update` | Live queue parameter tuning |
| Matchmaking | `/matchmaking/queue/delete` | Destructive: cancels all tickets in queue |
| Account | `/account/create` | Direct account creation bypassing Auth flow |
| Account | `/account/delete` | Account deletion (high-impact, irreversible) |
| Account | `/account/roles/bulk-update` | Bulk privilege changes |

**Rules**:
1. Admin endpoints have the highest blast radius of any WebSocket-accessible endpoint — treat every admin endpoint as potentially destructive
2. Admin endpoints MAY accept identifiers that user endpoints cannot (e.g., accountId for admin account management) — T32 exempts admin-boundary operations
3. Prefer `[]` (service-to-service) over `role: admin` when the endpoint is only called by automated systems — admin means "a human administrator may need to call this from a WebSocket session"
4. Admin endpoints SHOULD still validate input and enforce business rules — admin privilege does not bypass data integrity
5. **Endpoints that return sensitive data (password hashes, encryption keys, MFA secrets, internal tokens) MUST be `[]`, never `role: admin`** — sensitive data should never traverse a WebSocket connection, regardless of the caller's role. If a human admin needs to verify account existence, provide a separate admin endpoint that returns non-sensitive fields only

**Admin vs Service-to-Service decision**: "Does a human administrator ever need to call this from a dashboard/CLI connected via WebSocket?" If yes → Admin. If the endpoint is only ever called by other services programmatically → Service-to-Service (`[]`).

**The dual-endpoint pattern**: When an entity needs both admin dashboard access AND service-to-service access with different data exposure, create two endpoints. Example: `/account/get` (admin, returns non-sensitive account data for dashboards) and `/account/by-email` (service-to-service `[]`, returns password hash for Auth login verification). The admin endpoint should never return data that would be dangerous if intercepted over WebSocket.

---

## Level 6: Service-to-Service

```yaml
x-permissions: []
```

**What it means**: The endpoint is excluded from the permission matrix entirely. No WebSocket client — regardless of role — receives a GUID for this endpoint. It is invisible to all WebSocket sessions. Only accessible via lib-mesh generated clients (service-to-service calls).

**When to use**: For endpoints that are internal plumbing — called by other Bannou services as part of their business logic, never by human users or external clients.

**Examples**:

| Service | Endpoint | Why Service-to-Service |
|---------|----------|----------------------|
| Permission | `/permission/register-service` | Called by plugins at startup |
| Permission | `/permission/update-session-state` | Called by GameSession, Matchmaking |
| Subscription | All endpoints | Subscription state is internal to game access control |
| Resource | All endpoints | Reference tracking is infrastructure |
| Achievement | `/achievement/progress/update` | Called by Analytics event handlers |
| Divine | All endpoints (current) | Orchestration layer for god-actors |
| Connect | `/connect/broadcast` | Internal multi-node relay |

**Rules**:
1. Service-to-service endpoints are the **default** — if you're unsure whether an endpoint needs WebSocket access, start with `[]` and add a role only when you have a concrete use case
2. "Internal-only" in a deep dive description means the service has no anonymous/browser-facing endpoints, NOT that all endpoints use `[]` — services described as "internal-only" may still have `role: user` or `role: admin` endpoints for authenticated WebSocket access
3. An entire service being service-to-service (`[]` on all endpoints) is common and correct for infrastructure and orchestration services
4. Service-to-service calls bypass the permission system entirely — the calling service is trusted by virtue of being loaded in the same deployment
5. Endpoints that accept `accountId` as input (violating T32 for user endpoints) are often correct as service-to-service — the calling service resolves the accountId from its own context

**The "who calls this" test**: Trace every caller of the endpoint. If every caller is another Bannou service (via generated client), the endpoint is service-to-service. If even one caller is a WebSocket client, the endpoint needs a role.

### When services use mixed permission levels

Many services have a mix of service-to-service and user-facing endpoints. This is normal and expected:

| Service | User Endpoints | Service-to-Service Endpoints |
|---------|---------------|------------------------------|
| Account | profile/update, password/update, mfa/update | create, delete, by-email, by-provider, batch-get |
| Achievement | list, get, get-progress | progress/update, unlock, rarity/recalculate |
| GameSession | join, leave | create (called by Matchmaking), publish-shortcut |

The principle: **each endpoint gets the permission level appropriate to its callers**, independent of what other endpoints on the same service use.

---

## Level 7: Browser-Facing (T15 Exception)

Browser-facing endpoints are a special case defined by T15. They are routed through NGINX directly, NOT through the WebSocket binary protocol. They use GET + path parameters (violating Bannou's POST-only pattern) because browsers need bookmarkable URLs, SEO, caching, and OAuth redirect compatibility.

**Current browser-facing endpoints** (complete list — additions require explicit justification):

| Service | Endpoints | Why Browser-Facing |
|---------|-----------|-------------------|
| Website | All `/website/*` | Public website: SEO, caching, bookmarks |
| Auth | `/auth/oauth/{provider}/init` | OAuth redirect flow requires browser navigation |
| Auth | `/auth/oauth/{provider}/callback` | OAuth provider redirects back to a URL |
| Connect | `/connect` (GET) | WebSocket upgrade is an HTTP GET handshake |
| Documentation | `/documentation/view/{slug}` | Browser-rendered markdown-to-HTML |
| Documentation | `/documentation/raw/{slug}` | Raw content fetch for embedding |

**Rules**:
1. Browser-facing endpoints are **exceptional** — the default is POST-only via WebSocket
2. Adding a new browser-facing endpoint requires justification: why can't this go through WebSocket?
3. Browser-facing endpoints do NOT appear in the WebSocket capability manifest — they are a separate routing layer
4. Authentication for browser-facing endpoints uses cookies or query parameters, not the WebSocket session
5. Browser-facing endpoints are declared via `x-controller-only: true` or standard REST conventions in the schema

---

## The Shortcut Pattern (Orthogonal to Permission Levels)

Shortcuts are **not a permission level** — they are a delivery mechanism that works alongside Levels 2 and 3. A shortcut pre-binds an endpoint's request payload so the client can invoke it without constructing the request body.

### What shortcuts solve

1. **T32 compliance**: Client doesn't need to provide accountId, gameServiceId, or other server-known context
2. **Intent clarity**: One shortcut = one specific action (e.g., "join Arcadia lobby"), no ambiguity
3. **Dynamic availability**: Shortcuts appear/disappear as context changes (subscribe to a game → join shortcut appears)
4. **Singleton semantics**: Each shortcut maps to exactly one context — you can't have two "join lobby" shortcuts for different games active simultaneously per session

### How shortcuts interact with permissions

- The underlying endpoint has `role: user` (or `role: user` + states)
- The shortcut is published by a service when context is established (e.g., GameSession publishes join shortcuts when Subscription confirms access)
- Connect generates a unique GUID for the shortcut and adds it to the session's capability manifest
- The client calls the shortcut GUID; Connect rewrites it to the real endpoint GUID and injects the pre-bound payload
- When context ends, the shortcut is revoked (removed from capability manifest)

### When to design for shortcuts

An endpoint is **shortcut-eligible** when:

1. **The client shouldn't construct the request** — the payload contains server-side context (game IDs, session IDs, configuration) that the client doesn't independently know
2. **The action is contextually triggered** — "join this specific game" appears when you subscribe, not always
3. **There's exactly one valid invocation per context** — "accept this match" not "accept match #X" (the client only ever has one pending match)
4. **T32 would otherwise be violated** — if the natural request body would include accountId or similar identity data, a shortcut is the correct solution

### Shortcut-eligible endpoint design rules

1. The endpoint MUST work both via shortcut AND via direct service-to-service call — the implementation doesn't know which path was used
2. The request body should be a complete, self-contained operation — not dependent on client-side state
3. The endpoint's `x-permissions` must include `role: user` (or user + states) — shortcuts can only be published for endpoints that appear in the permission matrix
4. The service publishing the shortcut is responsible for revoking it when the context expires

### Examples

| Shortcut | Published By | Published When | Revoked When | Underlying Endpoint |
|----------|-------------|----------------|--------------|-------------------|
| "Join Arcadia Lobby" | GameSession | Account subscribes to Arcadia | Subscription expires | `/sessions/join` |
| "Leave Queue" | Matchmaking | Player joins queue | Player leaves queue or match forms | `/matchmaking/leave` |
| "Accept Match" | Matchmaking | Match is formed | Match is accepted/declined/expired | `/matchmaking/accept` |
| "Decline Match" | Matchmaking | Match is formed | Match is accepted/declined/expired | `/matchmaking/decline` |

---

## Decision Framework

Use this flowchart to determine the correct permission level for a new endpoint:

```
Is this endpoint accessed by web browsers directly (OAuth, HTML pages, WebSocket upgrade)?
  YES → Browser-Facing (Level 7, requires T15 justification)
  NO ↓

Is this endpoint called before the client has authenticated (login, register, password reset)?
  YES → Pre-Auth Public (Level 1, Auth service only)
  NO ↓

Is this endpoint ONLY called by other Bannou services via lib-mesh?
  YES → Service-to-Service (Level 6)
  NO ↓

Does a WebSocket client (human user) need to call this endpoint?
  NO → Service-to-Service (Level 6)
  YES ↓

Is this a system administration operation (infrastructure config, bulk ops, destructive)?
  YES → Admin (Level 5)
  NO ↓

Is this a content authoring or live operations tool (template CRUD, phase config, actor control)?
  YES → Developer (Level 4)
  NO ↓

Does this endpoint ONLY make sense when the player is in a specific state
(in a queue, in a match, in a game session)?
  YES → State-Gated User (Level 3)
  NO → Authenticated User (Level 2)
```

**After determining the level, also ask**: Should this endpoint be shortcut-eligible? Apply the shortcut pattern if the client shouldn't construct the request body, the action is contextually triggered, or T32 would otherwise be violated.

---

## Common Patterns by Service Type

### Pure Infrastructure Services (L0)

All endpoints `[]`. These services provide lib-* interfaces, not client-facing APIs.

*Examples*: State, Messaging, Mesh, Telemetry.

### Identity Boundary Services (L1 — Account, Auth)

Auth has anonymous endpoints for login bootstrapping and user endpoints for session management. Account has admin endpoints for identity CRUD (callable from admin dashboards) and user endpoints for self-service profile management. Most Account endpoints are admin-only because Auth mediates between clients and Account for security-sensitive operations.

### Gateway Services (L1 — Connect, Permission)

Mostly `[]`. These services manage the session infrastructure itself — clients interact with them indirectly (WebSocket protocol, capability manifests) rather than through explicit API calls.

### Game Foundation Services (L2)

Mix of `role: user` for player-facing CRUD and `[]` for internal coordination. State-gating is used when the service manages session-like contexts (GameSession uses `in_game` state). Services described as "internal-only" (Subscription, Resource) use `[]` on all endpoints.

### Content Definition Services (L4 — orchestration layers)

Mix of `role: user` for gameplay endpoints and `role: developer` for template/definition management. The pattern: players interact with instances, developers manage definitions.

*Example*: Gardener has `role: user` for garden/scenario gameplay and `role: developer` for template/phase management.

### Pure Orchestration Services (L4 — god-actor consumers)

All endpoints `[]`. These services are called by god-actors (via Puppetmaster/Actor ABML action handlers) or by other services. Players never directly invoke divine blessings, dungeon spawns, or storyline compositions — those happen through the actor system.

*Examples*: Divine, Puppetmaster, Storyline.

### Observation/Analytics Services (L4)

Player-facing read endpoints use `role: user` (view leaderboards, view achievements). Write/ingest endpoints use `[]` (called by event handlers, background workers). Admin endpoints use `role: admin` for configuration.

*Examples*: Analytics, Leaderboard, Achievement.

### Developer Tool Services (L4)

All endpoints `role: developer`. The entire service exists for the development team's use during live operations.

*Example*: Director (observe, steer, drive actors during live events).

---

## Anti-Patterns

### 1. Using `role: admin` when you mean `[]`

**Wrong**: "Only admins should call this, so I'll use `role: admin`."
**Right**: If no human ever calls it from a WebSocket session, use `[]`. Admin means a human administrator with a WebSocket connection. Service-to-service means automated programmatic access.

**Test**: Remove all admin WebSocket sessions from the system. Does the endpoint still get called? If yes → `[]`.

### 2. Using `role: anonymous` for read-only endpoints

**Wrong**: "This endpoint just returns public data, so it should be anonymous."
**Right**: Anonymous is for authentication bootstrapping only. Public data goes through Website (browser-facing) or is pushed via client events after authentication. Even read-only access reveals information and creates an attack surface.

### 3. State-gating on another service's internal state

**Wrong**: `states: { gardener: in_scenario }` on a Matchmaking endpoint.
**Right**: If Matchmaking needs to check garden state, it should do so in business logic via an API call, not by coupling to Gardener's Permission state. State-gating across services creates invisible dependencies.

### 4. Making all endpoints on an "internal-only" service use `[]`

**Wrong**: "The deep dive says internal-only, so everything is `x-permissions: []`."
**Right**: "Internal-only" means the service is not internet-facing (no anonymous endpoints, no browser-facing endpoints). It does NOT mean all endpoints are service-to-service. Account is "internal-only" but has `role: user` endpoints for self-service profile updates and `role: admin` endpoints for admin dashboard access.

### 5. Skipping state-gating because "the business logic handles it"

**Wrong**: "The endpoint returns 404 if you're not in a queue, so state-gating is redundant."
**Right**: State-gating serves a different purpose than business-logic validation. It controls the **client's capability manifest** — the client literally doesn't know the endpoint exists when the state is inactive. This prevents UI confusion (no "Leave Queue" button when you're not in a queue) and reduces unnecessary network traffic. If an endpoint is **always invalid** outside a specific context, it MUST be state-gated. Business-logic validation (returning 404) is the safety net, not the access control mechanism.

### 6. Using shortcuts to bypass T32 instead of fixing the endpoint design

**Wrong**: The endpoint accepts accountId, so you use a shortcut to hide it from the client.
**Right**: The endpoint should not accept accountId at all. The server resolves identity from the WebSocket session. Shortcuts pre-bind server-known context (game IDs, session IDs), not identity data.

### 7. Publishing shortcuts for endpoints that aren't shortcut-eligible

**Wrong**: Publishing a shortcut for `/account/profile/update` — the client needs to provide the new display name, which can't be pre-bound.
**Right**: Shortcuts are for endpoints where the entire request can be pre-bound by the server. If the client must provide dynamic data, the endpoint is called directly, not via shortcut.

### 8. Exposing sensitive data over WebSocket via `role: admin`

**Wrong**: `/account/by-email` returns password hashes with `role: admin` — "only admins can see it."
**Right**: Password hashes, MFA secrets, encryption keys, and internal tokens MUST NEVER traverse a WebSocket connection. Use `[]` for endpoints that return sensitive data. If an admin dashboard needs account lookup, provide a separate admin endpoint that excludes sensitive fields. The WebSocket transport is a wider attack surface than internal lib-mesh calls — even admin sessions can be compromised, and WebSocket traffic may be logged or intercepted differently than internal service communication.

### 9. Using the same permission level for all endpoints on a service

**Wrong**: "Account is an admin service, so all endpoints are `role: admin`."
**Right**: Each endpoint gets the permission level appropriate to **its own callers and data**, independent of the service's overall character. A service can have `role: user` (self-service profile update), `role: admin` (account listing for dashboards), and `[]` (password hash lookup for Auth) all on the same service. The principle is per-endpoint, not per-service.

---

## Summary: Permission Level by Question

| Question | Answer |
|----------|--------|
| Can unauthenticated clients call this? | Pre-Auth Public (`role: anonymous`) |
| Can any logged-in player call this anytime? | Authenticated User (`role: user`) |
| Can players call this only in a specific context? | State-Gated User (`role: user` + states) |
| Can content authors / live ops call this? | Developer (`role: developer`) |
| Can system administrators call this? | Admin (`role: admin`) |
| Is this only called by other services? | Service-to-Service (`[]`) |
| Is this accessed by web browsers? | Browser-Facing (T15 exception) |

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-03-06 | 1.0 | Initial version: 7 permission levels, shortcut pattern, decision framework |
| 2026-03-06 | 1.1 | Mandatory Entry/Context/Exit pattern for state-gating; sensitive data rule for admin endpoints; removed false Gardener exception; added anti-patterns #8 (sensitive data over WebSocket) and #9 (per-service vs per-endpoint permissions); dual-endpoint pattern for mixed admin/service-to-service access |

---

*This document is referenced by T13 (FOUNDATION.md) and SCHEMA-RULES.md. Updates require explicit approval.*
