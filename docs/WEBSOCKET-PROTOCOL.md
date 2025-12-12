# Bannou WebSocket Protocol Specification

This document describes the binary WebSocket protocol used by Bannou's Connect service edge gateway for zero-copy message routing and efficient client-server communication.

## Overview

Bannou uses a **hybrid binary header + JSON payload** protocol optimized for:
- Zero-copy message routing (Connect service extracts header without parsing payload)
- Client-specific security via session-salted GUIDs
- Channel multiplexing for fair message scheduling
- Request/response correlation for bidirectional RPC

## Binary Header Format (31 bytes)

Every WebSocket message begins with a 31-byte binary header:

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

### Header Fields

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0 | 1 | Flags | Message behavior flags (see below) |
| 1-2 | 2 | Channel | Channel ID for message ordering (0 = default) |
| 3-6 | 4 | Sequence | Per-channel sequence number |
| 7-22 | 16 | Service GUID | Client-salted GUID identifying target service |
| 23-30 | 8 | Message ID | Unique ID for request/response correlation |

## Message Flags (Byte 0)

```
Bit 7  Bit 6  Bit 5  Bit 4  Bit 3  Bit 2  Bit 1  Bit 0
 ▼      ▼      ▼      ▼      ▼      ▼      ▼      ▼
┌──────┬──────┬──────┬──────┬──────┬──────┬──────┬──────┐
│Rsvd  │Resp  │Client│Event │HiPri │Compr │Encr  │Binary│
└──────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┘
```

| Flag | Hex | Description |
|------|-----|-------------|
| None | 0x00 | Default: JSON payload, service request, expects response |
| Binary | 0x01 | Payload is binary data (not UTF-8 JSON) |
| Encrypted | 0x02 | Payload is encrypted |
| Compressed | 0x04 | Payload is gzip compressed |
| HighPriority | 0x08 | Skip to front of processing queues |
| Event | 0x10 | Fire-and-forget, no response expected |
| Client | 0x20 | Route to another WebSocket client (P2P) |
| Response | 0x40 | This is a response to a request |
| Reserved | 0x80 | Reserved for future use |

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

Server sends available APIs with client-salted GUIDs:
```json
{
  "type": "capabilities",
  "sessionId": "abc123",
  "services": {
    "accounts": {
      "guid": "550e8400-e29b-41d4-a716-446655440000",
      "endpoints": ["GET:/accounts/{id}", "PUT:/accounts/{id}"]
    },
    "auth": {
      "guid": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "endpoints": ["POST:/auth/logout", "GET:/auth/sessions"]
    }
  },
  "version": 42
}
```

### 4. Binary Messaging

After authentication, all messages use the 31-byte binary header:

**Client Request:**
```
[31-byte header][JSON payload]

Header:
  Flags: 0x00 (JSON, request, expects response)
  Channel: 0
  Sequence: 1
  GUID: <accounts service GUID>
  MessageId: 0x0123456789ABCDEF

Payload:
{"accountId": "user123"}
```

**Server Response:**
```
[31-byte header][JSON payload]

Header:
  Flags: 0x40 (Response flag set)
  Channel: 0
  Sequence: 1 (matches request)
  GUID: <same GUID>
  MessageId: 0x0123456789ABCDEF (matches request)

Payload:
{"id": "user123", "email": "user@example.com"}
```

### 5. Capability Updates

When permissions change, server pushes updated capabilities:
```json
{
  "type": "capabilities",
  "updateType": "full",
  "services": { ... },
  "version": 43
}
```

### 6. Connection Close

Standard WebSocket close with optional reason code.

## Message Routing

### Client-to-Service

1. Client sends binary message with service GUID
2. Connect service extracts GUID (zero-copy)
3. Looks up service name from GUID mapping
4. Routes to Dapr service via service invocation
5. Returns response with same Message ID

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

## Response Codes

Responses include standard HTTP-like codes:

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Bad Request |
| 401 | Unauthorized |
| 403 | Forbidden |
| 404 | Not Found |
| 500 | Internal Server Error |

## Error Handling

**Timeout:** 30 seconds default for request/response correlation
**Reconnection:** Clients should implement exponential backoff
**Sequence Gaps:** Server may request retransmission for critical messages

## Implementation Files

- `/lib-connect/Protocol/BinaryMessage.cs` - Message structure and parsing
- `/lib-connect/Protocol/MessageFlags.cs` - Flag definitions
- `/lib-connect/Protocol/GuidGenerator.cs` - GUID generation
- `/lib-connect/Protocol/ConnectionState.cs` - Connection lifecycle
- `/lib-connect/Protocol/NetworkByteOrder.cs` - Byte order utilities

## Security Considerations

1. **JWT Validation**: All connections require valid JWT
2. **Session Isolation**: Client-salted GUIDs prevent cross-session attacks
3. **SHA256 Hashing**: Cryptographic security for GUID generation
4. **Server Salt**: Per-instance salt adds entropy
5. **Permission Validation**: All API calls validated against capabilities
