# Why Does Bannou Use Client-Salted GUIDs Instead of Endpoint URLs?

> **Short Answer**: Because fixed endpoint URLs are a security liability in a persistent-connection architecture. If every client uses the same URL for the same endpoint, a captured message from one session can be replayed against another. Client-salted GUIDs make each session's endpoint identifiers unique, ephemeral, and cryptographically useless outside their originating session.

---

## How Endpoint Routing Normally Works

In a REST API, the client knows endpoint URLs: `POST /account/get`, `POST /auth/login`, `POST /character/create`. These URLs are static, documented, and identical for every client. The server distinguishes between clients using authentication tokens (JWTs, API keys, session cookies) attached to each request.

This works well for stateless HTTP because each request is independent and fully self-contained. The URL identifies the operation. The token identifies the caller.

---

## Why This Breaks for WebSocket

In Bannou's WebSocket architecture, the client sends binary-framed messages through a persistent connection. Each message contains a 31-byte header with a 16-byte Service GUID that identifies the target endpoint. The Connect gateway reads this GUID and routes the message to the appropriate backend service.

If the GUID were the same for every client -- say, the GUID for `/account/get` is always `a1b2c3d4-...` -- then:

1. **Replay attacks**: An attacker who captures a message from Client A (via network sniffing, compromised proxy, or client-side exploit) has a valid message structure. If Client B uses the same GUIDs, the attacker can inject Client A's captured message into Client B's connection (or a newly established connection using stolen credentials).

2. **Endpoint enumeration**: A malicious client can try all possible GUIDs to discover which endpoints exist, even ones not in their capability manifest. With static GUIDs, the space to enumerate is fixed and shared across all clients.

3. **Cross-session message injection**: In a multi-instance deployment, if an attacker gains access to the message bus (RabbitMQ) or a routing layer, static GUIDs mean any captured message is valid for any session. The GUID does not bind the message to a specific session.

---

## How Client-Salted GUIDs Work

When a client establishes a WebSocket connection, the Connect service generates a **session-specific salt**. Each canonical endpoint GUID is combined with this salt to produce a unique GUID for that session:

```
Canonical GUID for /account/get: a1b2c3d4-e5f6-7890-abcd-ef1234567890
Client A's salt: [session-specific value]
Client A's GUID for /account/get: f9e8d7c6-b5a4-3210-fedc-ba0987654321

Client B's salt: [different session-specific value]
Client B's GUID for /account/get: 12345678-9abc-def0-1234-56789abcdef0
```

The same endpoint has a different GUID for every connected client. The salt changes on reconnection. A captured GUID from Client A is meaningless for Client B -- it does not map to any endpoint in Client B's session.

---

## The Capability Manifest

The client learns its session-specific GUIDs through the **capability manifest** delivered over the WebSocket connection immediately after authentication:

```json
{
  "capabilities": [
    {
      "name": "account/get",
      "guid": "f9e8d7c6-b5a4-3210-fedc-ba0987654321"
      //, ...
    },
    {
      "name": "character/create",
      "guid": "aabbccdd-eeff-0011-2233-445566778899"
      //, ...
    }
  ]
}
```

The manifest is sent over the authenticated, encrypted WebSocket connection. It only contains endpoints the client has permission to access (based on their precompiled permission manifest). When permissions change, an updated manifest is pushed.

This means:

- **Clients never hard-code endpoint paths or GUIDs.** They discover available operations dynamically.
- **The server controls what the client can see.** Unpermitted endpoints are not just forbidden -- they are invisible. The client has no GUID to even attempt to call them.
- **GUIDs are ephemeral.** Compromise of a manifest is limited to one session. Reconnection generates new GUIDs.

---

## The Zero-Copy Routing Connection

Client-salted GUIDs tie directly into Connect's zero-copy routing. The gateway maintains a map from salted GUID to backend service endpoint for each session. When a message arrives:

1. Read 16-byte GUID from the binary header.
2. Look up the GUID in the session's routing table.
3. Forward the JSON payload to the resolved endpoint without deserializing it.

Because the GUID is session-specific, step 2 also implicitly validates that the message belongs to this session. A GUID from a different session will not exist in this session's routing table and will be rejected before any payload processing occurs.

In a URL-based system, the gateway would need to:
1. Parse the URL from the message.
2. Validate the URL against the session's permitted endpoints.
3. Route based on the URL path.

This is more work per message and requires the gateway to understand URL structure. With GUIDs, the lookup is a constant-time hash map operation that simultaneously routes and validates.

---

## The Trade-Off

Client-salted GUIDs make debugging harder. You cannot look at a captured message and immediately tell which endpoint it targets -- you need the session's routing table to resolve the GUID. Standard HTTP debugging tools (curl, Postman, browser DevTools) do not work with this protocol.

Bannou accepts this trade-off because:

- **Development tooling compensates.** Swagger UI is available for HTTP-based testing. The edge-tester project provides structured WebSocket testing. The binary protocol is documented. Every service also exposes its meta endpoints via standard HTTP GET, so direct service testing during development works with conventional tools.
- **Meta endpoints work through the same protocol.** Clients can request runtime schema introspection for any endpoint by sending a message with the Meta flag (0x40) set and the endpoint's salted GUID. Connect intercepts the meta request and returns the endpoint's JSON schema -- request format, response format, operation metadata -- through the same binary protocol. This means even with opaque GUIDs, a client SDK can dynamically discover not just *what* endpoints exist (via the capability manifest) but *what they accept and return* (via meta requests). The GUIDs are opaque for security, but the protocol is fully self-describing for functionality.
- **Security is load-bearing.** In a system where 100,000+ concurrent connections share the same infrastructure, cross-session message injection is a real threat, not a theoretical concern.
- **The manifest is self-documenting.** Clients do not need external API documentation to discover available endpoints. The manifest tells them what exists, meta requests tell them how to call it, and both update in real time.

The debugging inconvenience is bounded to development time. The security benefit applies to every message in production, permanently.
