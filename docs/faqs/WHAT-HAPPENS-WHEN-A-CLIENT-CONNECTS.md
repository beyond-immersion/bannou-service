# What Happens When a Client Connects to Bannou?

> **Short Answer**: The client authenticates via HTTP, receives a JWT and a WebSocket URL, establishes a persistent WebSocket connection, gets a session-specific capability manifest pushed to it, and from that point forward all communication flows through the binary-framed WebSocket protocol. Five L1 services coordinate to make this happen.

---

## The Full Connection Flow

The connection flow touches every L1 service except Contract and Resource. Understanding it reveals why these services are separate and how they cooperate.

### Step 1: Authentication (Auth Service)

The client authenticates via one of several mechanisms, all through HTTP:

- **Email/password**: `POST /auth/login` with credentials. If MFA is enabled, a challenge token is returned and the client completes `POST /auth/mfa/verify`.
- **OAuth provider**: Browser redirects through Discord/Google/Twitch OAuth flow. Auth handles the callback, exchanges the code for a token, and creates or links the account.
- **Steam**: Client sends a Steam session ticket. Auth validates it against the Steam Web API.

On success, Auth:
1. Creates or retrieves the account via the Account service (`IAccountClient.GetByEmailAsync`, `IAccountClient.CreateAsync`).
2. Creates a session in Redis with the account's roles, authorizations, and expiry.
3. Generates a JWT containing the session ID, account ID, roles, and expiry.
4. Returns the JWT and a WebSocket connect URL to the client.

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "connect_url": "wss://example.com/ws",
  "session_id": "abc123",
  "expires_at": "2026-02-12T00:00:00Z"
}
```

**Services involved**: Auth (primary), Account (via mesh).

### Step 2: WebSocket Connection (Connect Service)

The client opens a WebSocket connection to the provided URL, sending the JWT as part of the connection handshake.

Connect:
1. Validates the JWT by calling Auth (`IAuthClient.ValidateTokenAsync`).
2. Creates a `ConnectionStateData` entry in Redis with the session ID, account ID, roles, authorizations, and timestamps.
3. Generates a **session-specific salt** and computes client-salted GUIDs for every registered endpoint.
4. Sets up a **per-session RabbitMQ subscription** (`CONNECT_SESSION_{sessionId}`) for server-to-client event delivery.
5. Publishes a `session.connected` event to notify other services.

**Services involved**: Connect (primary), Auth (validation via mesh).

### Step 3: Permission Compilation (Permission Service)

Permission receives the `session.connected` event and:
1. Adds the session to its `active_sessions` and `active_connections` sets in Redis.
2. Looks up the session's roles and states.
3. Evaluates the multi-dimensional permission matrix (service x state x role) for every registered service.
4. Compiles the results into a flat set of allowed endpoint identifiers.
5. Pushes a `SessionCapabilitiesEvent` to the session's RabbitMQ queue via `IClientEventPublisher`.

**Services involved**: Permission (primary), Connect (receives the pushed event via RabbitMQ).

### Step 4: Capability Manifest Delivery (Connect Service)

Connect receives the `SessionCapabilitiesEvent` on the session's RabbitMQ queue and:
1. Builds a capability manifest mapping endpoint names to client-salted GUIDs, filtered to only include endpoints the session is permitted to access.
2. Serializes the manifest and sends it to the client over the WebSocket connection.

The client now has a complete map of every API it can call, with session-specific GUIDs for each.

**Services involved**: Connect (primary).

### Step 5: Steady-State Communication

From this point, the client communicates exclusively through binary-framed WebSocket messages:

```
Client sends: [31-byte binary header | JSON payload]
    -> Connect reads GUID from header
    -> Connect looks up GUID in session routing table
    -> Connect forwards JSON to target service via lib-mesh
    -> Target service processes request, returns response
    -> Connect sends response back to client via WebSocket
```

Server-push events (permission updates, game shortcuts, match notifications) arrive through the per-session RabbitMQ queue and are forwarded to the client by Connect.

---

## The Reconnection Flow

If the WebSocket connection drops (network interruption, app backgrounding):

1. Connect detects the disconnection and publishes `session.disconnected`.
2. Connect preserves the session state in Redis for a configurable reconnection window.
3. Connect generates a **reconnection token** and stores it in Redis with a TTL matching the reconnection window.
4. If the client reconnects within the window, it provides the reconnection token.
5. Connect validates the token, restores the session (same GUIDs, same permissions, same state), and publishes `session.reconnected`.

The reconnection is seamless -- the client does not need to re-authenticate or receive a new capability manifest. Other services (GameSession, Matchmaking) subscribe to `session.reconnected` to restore their own state.

---

## The Disconnection Flow

When a client disconnects permanently (logout, token expiry, connection timeout beyond the reconnection window):

1. Connect publishes `session.disconnected` with a reason.
2. Connect tears down the per-session RabbitMQ subscription.
3. Connect removes the session from its in-memory connection state and Redis.
4. Permission receives the event and removes the session from `active_connections`.
5. Other services (GameSession, Matchmaking, Actor) receive the event and clean up session-specific state.

If the disconnection is triggered by Auth (session invalidation, account deletion):

1. Auth publishes `session.invalidated`.
2. Connect receives the event and force-disconnects the WebSocket client.
3. The normal disconnection flow follows.

---

## Why This Matters

The connection flow demonstrates why the L1 services are separate:

- **Auth** handles the security-critical internet-facing authentication step. It does not need to know about WebSockets, GUIDs, or capability manifests.
- **Account** provides the durable user record. It does not participate in the connection flow beyond being queried by Auth.
- **Connect** manages the transport layer -- WebSocket lifecycle, binary routing, RabbitMQ subscriptions. It does not make authentication decisions.
- **Permission** compiles authorization manifests. It does not manage connections or validate tokens.

Each service contributes one step to the flow and does not need to understand the other steps. Auth does not know that its JWT will be used for a WebSocket connection (it could be used for HTTP API calls instead). Connect does not know how the JWT was generated (email/password or OAuth). Permission does not know whether the session is a WebSocket or HTTP session (it compiles manifests either way).

This separation means each step can evolve independently. Auth can add new authentication methods without touching Connect. Permission can change its matrix model without touching Auth. Connect can change its binary protocol without affecting either. The connection flow is a pipeline of independent stages, not a monolithic procedure.
