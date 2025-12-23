# Bannou WebSocket Protocol Specification

This document describes the binary WebSocket protocol used by Bannou's Connect service edge gateway for zero-copy message routing and efficient client-server communication.

## Overview

Bannou uses a **hybrid binary header + JSON payload** protocol optimized for:
- Zero-copy message routing (Connect service extracts header without parsing payload)
- Client-specific security via session-salted GUIDs
- Channel multiplexing for fair message scheduling
- Request/response correlation for bidirectional RPC
- Compact response headers (16 bytes vs 31-byte request headers)

## Request Header Format (31 bytes)

Client requests use a 31-byte binary header:

```
┌─────────────────────────────────────────────────────────────────┐
│ Byte 0:    Message Flags (1 byte)                               │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 1-2: Channel ID (2 bytes, big-endian uint16)              │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 3-6: Sequence Number (4 bytes, big-endian uint32)         │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 7-22: Service GUID (16 bytes, network byte order)         │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 23-30: Message ID (8 bytes, big-endian uint64)            │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 31+:  Payload (variable length - JSON or binary)          │
└─────────────────────────────────────────────────────────────────┘
```

### Request Header Fields

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0 | 1 | Flags | Message behavior flags (see below) |
| 1-2 | 2 | Channel | Channel ID for message ordering (0 = default) |
| 3-6 | 4 | Sequence | Per-channel sequence number |
| 7-22 | 16 | Service GUID | Client-salted GUID identifying target service |
| 23-30 | 8 | Message ID | Unique ID for request/response correlation |

## Response Header Format (16 bytes)

Server responses use a compact 16-byte binary header. The Service GUID is omitted because the client already knows which service it called (correlated via Message ID).

```
┌─────────────────────────────────────────────────────────────────┐
│ Byte 0:    Message Flags (1 byte, Response flag 0x40 set)       │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 1-2: Channel ID (2 bytes, big-endian uint16)              │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 3-6: Sequence Number (4 bytes, big-endian uint32)         │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 7-14: Message ID (8 bytes, big-endian uint64)             │
├─────────────────────────────────────────────────────────────────┤
│ Byte 15:   Response Code (1 byte, protocol code 0-255)          │
├─────────────────────────────────────────────────────────────────┤
│ Bytes 16+: Payload (success: JSON data, error: empty)           │
└─────────────────────────────────────────────────────────────────┘
```

### Response Header Fields

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0 | 1 | Flags | Response flag (0x40) is set |
| 1-2 | 2 | Channel | Same as request for ordering |
| 3-6 | 4 | Sequence | Same as request for correlation |
| 7-14 | 8 | Message ID | Same as request for correlation |
| 15 | 1 | Response Code | Protocol response code (see table below) |

### Response Codes

Response codes are protocol-level codes (not HTTP codes). The client SDK maps these to HTTP status codes.

| Protocol Code | Name | HTTP Equivalent | Description |
|---------------|------|-----------------|-------------|
| 0 | OK | 200 | Success - payload contains response data |
| 50 | Service_BadRequest | 400 | Invalid request format or parameters |
| 51 | Service_NotFound | 404 | Requested resource not found |
| 52 | Service_Unauthorized | 401 | Authentication required or failed |
| 53 | Service_Conflict | 409 | Resource conflict (e.g., duplicate) |
| 60 | Service_InternalServerError | 500 | Server-side error |

**Error Response Behavior**: For non-zero response codes, the payload is **empty**. The response code in byte 15 tells the complete story. This keeps error responses minimal (16 bytes total).

## Message Flags (Byte 0)

```
Bit 7  Bit 6  Bit 5  Bit 4  Bit 3  Bit 2  Bit 1  Bit 0
 ▼      ▼      ▼      ▼      ▼      ▼      ▼      ▼
┌──────┬──────┬──────┬──────┬──────┬──────┬──────┬──────┐
│Meta  │Resp  │Client│Event │HiPri │Compr │Encr  │Binary│
└──────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┘
```

| Flag | Hex | Description |
|------|-----|-------------|
| None | 0x00 | Default: JSON payload, service request, expects response |
| Binary | 0x01 | Payload is binary data (not UTF-8 JSON) |
| Encrypted | 0x02 | Payload is encrypted (reserved for future use) |
| Compressed | 0x04 | Payload is gzip compressed (reserved for future use) |
| HighPriority | 0x08 | Skip to front of processing queues |
| Event | 0x10 | Fire-and-forget, no response expected |
| Client | 0x20 | Route to another WebSocket client (P2P) |
| Response | 0x40 | This is a response (uses 16-byte header format) |
| Meta | 0x80 | Request endpoint metadata instead of executing (see Meta Endpoints) |

## Client-Salted GUIDs

For security, each client session receives **unique GUIDs** for identical service endpoints. This prevents:
- Session hijacking via GUID theft
- Cross-session message routing attacks
- GUID enumeration attacks

### GUID Generation Algorithm

```
input = "service:{serviceName}|session:{sessionId}|salt:{serverSalt}"
hash = SHA256(UTF8(input))
guid = first_16_bytes(hash) with UUID v5 version bits
```

The server generates GUIDs and sends the mapping to clients as part of the capability manifest.

## Channel Multiplexing

Channels provide fair round-robin message scheduling:

| Channel | Purpose |
|---------|---------|
| 0 | Default channel (general API calls) |
| 1 | Authentication channel |
| 2 | Accounts channel |
| 3+ | Custom per-service channels |

Messages on the same channel are processed sequentially (per sequence number).
Messages on different channels are processed with fair scheduling.

## Connection Lifecycle

### 1. WebSocket Upgrade

```
GET /connect HTTP/1.1
Upgrade: websocket
Authorization: Bearer <JWT>
```

### 2. Authentication

Upon connection, client sends JWT as first text message:
```
AUTH <jwt_token>
```

The Connect service validates the JWT and establishes the session.

### 3. Capability Manifest

Server sends available API endpoints with client-salted GUIDs. Each endpoint gets a unique GUID that is salted with the session ID for security isolation:

```json
{
  "type": "capability_manifest",
  "sessionId": "abc123",
  "availableAPIs": [
    {
      "serviceGuid": "550e8400-e29b-41d4-a716-446655440000",
      "method": "POST",
      "path": "/accounts/get",
      "endpointKey": "POST:/accounts/get"
    },
    {
      "serviceGuid": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "method": "POST",
      "path": "/auth/logout",
      "endpointKey": "POST:/auth/logout"
    }
  ],
  "version": 1,
  "timestamp": 1703001234567
}
```

**Key Format**: The `endpointKey` format is `METHOD:/path` (e.g., `POST:/accounts/get`). This is the key clients use to look up the GUID for an endpoint. Service names are internal routing information and are NOT exposed to clients.

### 4. Binary Messaging

After authentication, all messages use binary headers. Requests use 31-byte headers, responses use 16-byte headers.

**Client Request (31-byte header + payload):**
```
Header (31 bytes):
  Byte 0:     0x00 (Flags: JSON, request, expects response)
  Bytes 1-2:  0x0000 (Channel: 0)
  Bytes 3-6:  0x00000001 (Sequence: 1)
  Bytes 7-22: <accounts service GUID>
  Bytes 23-30: 0x0123456789ABCDEF (MessageId)

Payload:
{"accountId": "user123"}
```

**Server Success Response (16-byte header + payload):**
```
Header (16 bytes):
  Byte 0:     0x40 (Flags: Response)
  Bytes 1-2:  0x0000 (Channel: 0, same as request)
  Bytes 3-6:  0x00000001 (Sequence: 1, same as request)
  Bytes 7-14: 0x0123456789ABCDEF (MessageId, same as request)
  Byte 15:    0x00 (ResponseCode: 0 = OK)

Payload:
{"id": "user123", "email": "user@example.com"}
```

**Server Error Response (16-byte header, no payload):**
```
Header (16 bytes):
  Byte 0:     0x40 (Flags: Response)
  Bytes 1-2:  0x0000 (Channel: 0)
  Bytes 3-6:  0x00000001 (Sequence: 1)
  Bytes 7-14: 0x0123456789ABCDEF (MessageId)
  Byte 15:    0x33 (ResponseCode: 51 = NotFound)

Payload: (empty)
```

### 5. Capability Updates

When permissions change, server pushes an updated capability manifest:
```json
{
  "type": "capability_manifest",
  "sessionId": "abc123",
  "availableAPIs": [...],
  "version": 2,
  "timestamp": 1703001234999,
  "reason": "permission_change"
}
```

The `reason` field indicates why the manifest was updated (e.g., `permission_change`, `service_update`).

### 6. Connection Close

Standard WebSocket close with optional reason code.

## Message Routing

### Client-to-Service

1. Client sends binary message with service GUID (31-byte header)
2. Connect service extracts GUID (zero-copy)
3. Looks up service name from GUID mapping
4. Routes to Dapr service via service invocation
5. Returns response with 16-byte header and same Message ID

### Client-to-Client (P2P)

1. Client sends message with `Client` flag (0x20) set
2. GUID identifies target client (generated from session IDs)
3. Connect service routes directly to target WebSocket
4. No service invocation involved

### Service-to-Client (Push)

1. Service publishes event to RabbitMQ channel `CONNECT_{sessionId}`
2. Connect service receives event
3. Creates binary message with `Event` flag (0x10)
4. Pushes to client's WebSocket

## Meta Endpoints

The Meta flag (0x80) enables runtime introspection of API endpoints. When set, Connect service routes to companion endpoints that return JSON Schema and endpoint metadata instead of executing the endpoint.

### Meta Type Selection (Channel Field Override)

When the Meta flag is set, the **Channel field** is repurposed to specify which type of metadata to return:

| Channel Value | Meta Type | Description |
|---------------|-----------|-------------|
| 0 | EndpointInfo | Human-readable endpoint description (summary, tags, deprecated) |
| 1 | RequestSchema | JSON Schema for the request body |
| 2 | ResponseSchema | JSON Schema for the response body |
| 3 | FullSchema | Complete schema (info + request + response combined) |

### Meta Request Flow

1. Client sends request with **Meta flag (0x80)** set
2. Client sets **Channel field** to desired meta type (0-3)
3. Client uses the **same Service GUID** as the target endpoint
4. Connect service intercepts and transforms the path:
   - Original: `POST:/accounts/get`
   - Transformed: `GET:/accounts/get/meta/{suffix}` where suffix is `info`, `request-schema`, `response-schema`, or `schema`
5. Routes to companion endpoint (always HTTP GET)
6. Returns MetaResponse with schema data

### Meta Request Example

**Request metadata for POST /accounts/get (response schema):**
```
Header (31 bytes):
  Byte 0:     0x80 (Flags: Meta)
  Bytes 1-2:  0x0002 (Channel: 2 = ResponseSchema)
  Bytes 3-6:  0x00000001 (Sequence: 1)
  Bytes 7-22: <accounts/get service GUID>
  Bytes 23-30: 0x0123456789ABCDEF (MessageId)

Payload: (empty - meta requests have no body)
```

**Response:**
```json
{
  "metaType": "response-schema",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "Accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
      "id": { "type": "string" },
      "email": { "type": "string" }
    }
  },
  "generatedAt": "2025-01-15T12:00:00Z",
  "schemaVersion": "1.0.0"
}
```

### Meta Response Model

| Field | Type | Description |
|-------|------|-------------|
| metaType | string | Type of metadata: `endpoint-info`, `request-schema`, `response-schema`, `full-schema` |
| endpointKey | string | Endpoint identifier: `METHOD:/path` |
| serviceName | string | Service that owns this endpoint |
| method | string | HTTP method (GET, POST, etc.) |
| path | string | Endpoint path |
| data | object | Metadata payload (varies by metaType) |
| generatedAt | string | UTC timestamp of response generation |
| schemaVersion | string | Assembly version for schema versioning |

### Implementation Notes

- Meta endpoints are **always HTTP GET** regardless of the original endpoint's method
- Schema data is **embedded at build time** via code generation
- The Channel field's normal ordering semantics are ignored when Meta flag is set
- Meta requests expect a response (not fire-and-forget)
- Meta endpoints are generated companion endpoints (e.g., `/path/meta/schema`)

## Error Handling

**Timeout:** 30 seconds default for request/response correlation
**Reconnection:** Clients should implement exponential backoff
**Sequence Gaps:** Server may request retransmission for critical messages

## Implementation Files

- `/lib-connect/Protocol/BinaryMessage.cs` - Message structure and parsing
- `/lib-connect/Protocol/MessageFlags.cs` - Flag definitions
- `/lib-connect/Protocol/MetaType.cs` - Meta endpoint type enumeration
- `/lib-connect/Protocol/GuidGenerator.cs` - GUID generation
- `/lib-connect/Protocol/ConnectionState.cs` - Connection lifecycle
- `/lib-connect/Protocol/NetworkByteOrder.cs` - Byte order utilities
- `/lib-connect/Protocol/MessageRouter.cs` - Message routing logic

## Security Considerations

1. **JWT Validation**: All connections require valid JWT
2. **Session Isolation**: Client-salted GUIDs prevent cross-session attacks
3. **SHA256 Hashing**: Cryptographic security for GUID generation
4. **Server Salt**: Per-instance salt adds entropy
5. **Permission Validation**: All API calls validated against capabilities
6. **Empty Error Payloads**: Error responses don't leak server implementation details
