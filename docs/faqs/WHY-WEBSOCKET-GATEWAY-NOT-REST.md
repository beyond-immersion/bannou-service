# Why Does Bannou Route Everything Through a WebSocket Gateway Instead of Using REST?

> **Short Answer**: Because the system needs persistent, bidirectional, low-latency connections to support 100,000+ concurrent AI NPCs pushing real-time state to clients. REST is request-response -- the server cannot push to the client. WebSocket is bidirectional -- the server pushes capability updates, game events, NPC actions, and permission changes without the client polling.

---

## The REST Assumption

Most backend architectures use HTTP REST as their primary transport. Clients make requests, servers send responses. If the server needs to notify the client, you either poll (wasteful), use Server-Sent Events (unidirectional), or bolt on a WebSocket connection alongside the REST API (now you have two transports to maintain).

Bannou inverts this. The Connect service is a WebSocket-first edge gateway. After authentication via Auth (which uses traditional HTTP for OAuth callbacks and browser compatibility), the client establishes a single persistent WebSocket connection. All subsequent communication -- API calls, server-push events, permission updates, session shortcuts -- flows through this one connection using a hybrid binary-header/JSON-payload protocol.

The question is: why not just use REST like everyone else?

---

## The Server-Push Requirement

REST cannot push to the client. This matters enormously for a living world game backend.

Consider what Bannou needs to push to connected clients without being asked:

- **Permission updates**: When a player's role changes, joins a game session, enters a voice call, or gains new authorizations, the Permission service recompiles their capability manifest and pushes it immediately. The client's available API surface changes in real time.
- **Session shortcuts**: When a player subscribes to a game, GameSession publishes a "join game" shortcut to their connection. The client can present a one-click join button without ever querying for it.
- **Match found notifications**: When matchmaking finds a match, the accept/decline prompt must arrive at the client instantly. Polling at any interval means delayed match acceptance and degraded player experience.
- **NPC state changes**: When 100,000+ NPCs are making decisions every 100-500ms, the clients observing those NPCs need state updates pushed to them. Polling for NPC state is computationally impossible at this scale.
- **Session invalidation**: When Auth revokes a session (logout, security concern, admin action), the client must be disconnected immediately, not on their next poll cycle.
- **Error forwarding**: Admin clients receive service error events in real time for operational monitoring.

Each of these requires the server to initiate communication. REST has no mechanism for this. You would need a separate notification channel -- which is exactly what a WebSocket connection is, except now you have two transports, two authentication flows, two connection lifecycle managers, and two sets of failure modes.

---

## The Zero-Copy Routing Design

The Connect service routes messages using a 31-byte binary header:

```
Flags (1) | Channel (2) | Sequence (4) | Service GUID (16) | Message ID (8)
```

The Service GUID maps a client request to exactly one backend endpoint. Connect reads the GUID from the binary header and forwards the JSON payload to the target service **without deserializing it**. This is zero-copy routing -- the Connect service never needs to understand what is inside the message.

This design requires static endpoint paths. Each endpoint is a POST to a fixed path (e.g., `/account/get`), and the path maps to exactly one 16-byte GUID. The request payload goes in the body.

REST's path-parameter convention (`GET /account/{accountId}`) breaks this model. The path varies per request, so you cannot map it to a single GUID. You would need to parse the path to extract the variable segments, which defeats zero-copy routing and adds latency to every message.

The POST-only API pattern is not a philosophical choice about REST purity. It is a direct consequence of needing to route messages via static GUIDs for performance at scale.

---

## The Client-Salted GUID Security Model

When a client connects, Connect generates a unique set of endpoint GUIDs for that session by salting the canonical endpoint GUIDs with the client's session key. This means:

- Client A's GUID for `/account/get` is different from Client B's GUID for `/account/get`.
- A captured GUID from Client A's traffic is useless for impersonating requests to Client B's session.
- GUIDs are ephemeral -- they change on reconnection.

This security model only works with persistent connections where the server knows the client's identity throughout the session. In REST, each request is independent. You would need to include session context in every request (typically via a header), and the server would need to validate it on every request. With WebSocket, the session is established once at connection time, and all subsequent messages inherit that session context automatically.

---

## The Capability Manifest

On connection, the client receives a dynamic capability manifest -- a list of all API endpoints available to them, each with its client-salted GUID, method, and authentication requirement. This manifest updates in real time as:

- The user authenticates (anonymous -> authenticated: more endpoints become available)
- Permissions change (admin role granted: admin endpoints appear)
- Session state changes (joined game: in-game endpoints activate)
- Services deploy updates (new version: new endpoints appear in manifest)

The client never needs to know endpoint URLs, paths, or versioning. It discovers available capabilities dynamically through the manifest. This is only possible with a persistent connection that can push manifest updates. In REST, the client would need to poll for manifest changes, or hard-code endpoint paths and hope they don't change.

---

## The Reconnection Window

WebSocket connections drop. Networks are unreliable. The Connect service handles this with a reconnection window:

1. Client disconnects (network interruption, brief app backgrounding).
2. Connect preserves the session state in Redis for a configurable grace period.
3. Client reconnects with a reconnection token.
4. Connect restores the session -- same GUIDs, same permissions, same state. No re-authentication required.

This creates a seamless experience for the player. In a REST world, "reconnection" is meaningless because every request is independent. But if you have supplementary WebSocket connections for push notifications alongside REST, you now need reconnection logic anyway -- plus the additional complexity of coordinating state between two transports.

---

## The Counter-Argument

The honest counter-argument is: REST is simpler, better understood, has better tooling, and works fine for most applications. Swagger, Postman, curl, browser DevTools -- the entire HTTP ecosystem is built for request-response. WebSocket tooling is comparatively primitive. Debugging a binary-header protocol is harder than debugging JSON over HTTP.

Bannou acknowledges this by keeping HTTP as a first-class transport for service-to-service communication (via lib-mesh) and for specific browser-facing endpoints (Website service uses traditional REST, Auth uses GET for OAuth callbacks). The WebSocket gateway is specifically for the client-to-server edge, where the combination of persistent connections, bidirectional communication, zero-copy routing, and real-time capability updates provides capabilities that REST fundamentally cannot.

The architecture is not "WebSocket instead of REST." It is "WebSocket for the client edge where bidirectional persistent connections are load-bearing, HTTP for everything else."

---

## Why Not gRPC / GraphQL / SSE?

**gRPC**: Excellent for service-to-service communication, but requires HTTP/2 and does not have universal browser support without a proxy layer. Bannou's lib-mesh uses direct HTTP invocation which provides similar benefits for internal calls. For the client edge, gRPC's streaming model could work, but the custom binary protocol gives Bannou more control over routing semantics.

**GraphQL**: Solves the "fetch exactly what you need" problem, which is not Bannou's problem. Bannou's endpoints are purpose-built POST operations, not general-purpose data queries. GraphQL's subscription model could handle server-push, but it adds query parsing overhead to every message and doesn't support zero-copy routing.

**Server-Sent Events (SSE)**: Unidirectional (server-to-client only). The client would still need a separate mechanism for sending requests. SSE over HTTP/2 is viable, but you end up building a bidirectional protocol on top of two unidirectional channels -- which is what WebSocket already is, natively.

The Connect gateway exists because the specific combination of requirements -- binary routing, client-salted security, bidirectional persistent connections, real-time capability manifests, and zero-copy message forwarding -- is not well-served by any off-the-shelf transport. The protocol is custom because the requirements are custom.
