# Bannou Binary Protocol Reference

This document provides a detailed specification of the Bannou WebSocket binary protocol for Unreal Engine integration.

## Protocol Overview

Bannou uses a hybrid binary/JSON protocol:
- **Binary header**: Fixed-size for zero-copy routing (31 bytes request, 16 bytes response)
- **JSON payload**: Variable-size UTF-8 encoded request/response body

All multi-byte integers use **big-endian (network) byte order**.

## Message Types

### Request Message (31-byte header)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|     Flags     |           Channel             |               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+               +
|                      Sequence Number                          |
+                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                               |                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+                               +
|                                                               |
+                                                               +
|                          Service GUID                         |
+                                                               +
|                        (16 bytes, RFC 4122)                   |
+                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                               |                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+                               +
|                          Message ID                           |
+                        (64-bit big-endian)                    +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         JSON Payload                          |
|                           (variable)                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

| Offset | Size | Field | Type | Description |
|--------|------|-------|------|-------------|
| 0 | 1 | Flags | uint8 | Message behavior flags |
| 1 | 2 | Channel | uint16 BE | Message ordering channel (0 = default) |
| 3 | 4 | Sequence | uint32 BE | Per-channel sequence number |
| 7 | 16 | ServiceGuid | GUID | Client-salted endpoint GUID |
| 23 | 8 | MessageId | uint64 BE | Request/response correlation ID |
| 31 | N | Payload | bytes | JSON UTF-8 (or binary if Binary flag set) |

### Response Message (16-byte header)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|     Flags     |           Channel             |               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+               +
|                      Sequence Number                          |
+               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|               |                                               |
+-+-+-+-+-+-+-+-+                                               +
|                          Message ID                           |
+                        (64-bit big-endian)                    +
|                                                               |
+               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|               | ResponseCode  |                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+                               +
|                         JSON Payload                          |
|                  (empty for error responses)                  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

| Offset | Size | Field | Type | Description |
|--------|------|-------|------|-------------|
| 0 | 1 | Flags | uint8 | Response flag (0x40) always set |
| 1 | 2 | Channel | uint16 BE | Same as request channel |
| 3 | 4 | Sequence | uint32 BE | Same as request sequence |
| 7 | 8 | MessageId | uint64 BE | Same as request MessageId |
| 15 | 1 | ResponseCode | uint8 | 0 = OK, non-zero = error |
| 16 | N | Payload | bytes | JSON response (empty on error) |

## Message Flags

| Flag | Value | Description |
|------|-------|-------------|
| None | 0x00 | Default: JSON, expects response |
| Binary | 0x01 | Payload is binary (not JSON) |
| Encrypted | 0x02 | Payload is encrypted |
| Compressed | 0x04 | Payload is gzip compressed |
| HighPriority | 0x08 | Skip to front of processing queue |
| Event | 0x10 | Fire-and-forget, no response expected |
| Client | 0x20 | Route to another WebSocket client |
| Response | 0x40 | This is a response (not a request) |
| Meta | 0x80 | Request endpoint metadata |

## Response Codes

### Protocol-Level (0-49)

| Code | Name | Description |
|------|------|-------------|
| 0 | OK | Request completed successfully |
| 10 | RequestError | Malformed message or invalid format |
| 11 | RequestTooLarge | Payload exceeds maximum size |
| 12 | TooManyRequests | Rate limit exceeded |
| 13 | InvalidRequestChannel | Invalid channel number |
| 20 | Unauthorized | Authentication required or invalid |
| 30 | ServiceNotFound | GUID not in capability manifest |
| 31 | ClientNotFound | Target client GUID not found |
| 32 | MessageNotFound | Referenced message ID not found |
| 40 | BroadcastNotAllowed | Broadcast blocked in this mode |

### Service-Level (50-69)

| Code | Name | Description |
|------|------|-------------|
| 50 | Service_BadRequest | Service returned 400 |
| 51 | Service_NotFound | Service returned 404 |
| 52 | Service_Unauthorized | Service returned 401/403 |
| 53 | Service_Conflict | Service returned 409 |
| 60 | Service_InternalServerError | Service returned 500 |

### Shortcut-Specific (70+)

| Code | Name | Description |
|------|------|-------------|
| 70 | ShortcutExpired | Shortcut has expired |
| 71 | ShortcutTargetNotFound | Shortcut target no longer exists |
| 72 | ShortcutRevoked | Shortcut was explicitly revoked |

## GUID Serialization (RFC 4122)

GUIDs are serialized in RFC 4122 network byte order. This differs from how some platforms (like .NET) store GUIDs internally.

### GUID Layout (16 bytes)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                          time_low                             |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       time_mid                |    time_hi_and_version        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|clk_seq_hi_res |  clk_seq_low  |                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+                               +
|                             node                              |
+                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

The first three fields (time_low, time_mid, time_hi_and_version) are stored in big-endian order. The remaining bytes (clock sequence and node) are stored as-is.

### Unreal FGuid Mapping

```cpp
// FGuid has 4 uint32 components: A, B, C, D
// Mapping to RFC 4122:
//   A = time_low
//   B = (time_mid << 16) | time_hi_and_version
//   C = (clk_seq_hi_res << 24) | (clk_seq_low << 16) | (node[0] << 8) | node[1]
//   D = (node[2] << 24) | (node[3] << 16) | (node[4] << 8) | node[5]

void WriteGuid(uint8* Buffer, int32 Offset, const FGuid& Guid)
{
    // time_low (big-endian from A)
    Buffer[Offset + 0] = (Guid.A >> 24) & 0xFF;
    Buffer[Offset + 1] = (Guid.A >> 16) & 0xFF;
    Buffer[Offset + 2] = (Guid.A >> 8) & 0xFF;
    Buffer[Offset + 3] = Guid.A & 0xFF;

    // time_mid and time_hi_and_version (from B)
    Buffer[Offset + 4] = (Guid.B >> 24) & 0xFF;
    Buffer[Offset + 5] = (Guid.B >> 16) & 0xFF;
    Buffer[Offset + 6] = (Guid.B >> 8) & 0xFF;
    Buffer[Offset + 7] = Guid.B & 0xFF;

    // clock_seq and node bytes 0-1 (from C)
    Buffer[Offset + 8] = (Guid.C >> 24) & 0xFF;
    Buffer[Offset + 9] = (Guid.C >> 16) & 0xFF;
    Buffer[Offset + 10] = (Guid.C >> 8) & 0xFF;
    Buffer[Offset + 11] = Guid.C & 0xFF;

    // node bytes 2-5 (from D)
    Buffer[Offset + 12] = (Guid.D >> 24) & 0xFF;
    Buffer[Offset + 13] = (Guid.D >> 16) & 0xFF;
    Buffer[Offset + 14] = (Guid.D >> 8) & 0xFF;
    Buffer[Offset + 15] = Guid.D & 0xFF;
}
```

## Client-Salted GUIDs

Each client receives unique GUIDs for endpoints in their capability manifest. This prevents cross-client security exploits where one client could use another client's GUIDs.

```
Client A: POST:/account/get → GUID abc123...
Client B: POST:/account/get → GUID xyz789...  (different!)
```

The GUIDs are generated using a deterministic hash of:
- Base endpoint GUID (same for all clients)
- Client session ID (unique per connection)
- Server-side secret salt

## Channel Semantics

Channels (0-65535) provide message ordering guarantees:

- **Channel 0**: Default channel, general-purpose requests
- **Channel N > 0**: Messages within a channel are processed in sequence order

Use separate channels for:
- Game state synchronization (guaranteed order)
- Background operations (parallel processing OK)
- Real-time events (low latency, may skip ahead)

## Sequence Numbers

Each channel maintains an independent sequence counter:
- Start at 0 after connection
- Increment for each message on that channel
- Server may reject out-of-order messages (configurable)
- Used for duplicate detection and replay protection

## Message ID

64-bit unique identifier for request/response correlation:
- Client assigns MessageId to requests
- Server echoes MessageId in responses
- Use monotonically increasing counter or UUID

## Wire Examples

### Login Request

```
Flags:    0x00 (JSON, expects response)
Channel:  0x0000 (default channel)
Sequence: 0x00000001 (first message)
GUID:     [16 bytes for /auth/login endpoint]
MsgId:    0x0000000000000001

Payload:
{
  "email": "user@example.com",
  "password": "secret123"
}
```

Binary (hex):
```
00 00 00 00 00 00 01 [16-byte GUID] 00 00 00 00 00 00 00 01
7B 22 65 6D 61 69 6C 22 3A 22 75 73 65 72 40 65 78 61 6D 70 6C 65 2E 63 6F 6D 22 ...
```

### Success Response

```
Flags:    0x40 (Response)
Channel:  0x0000 (matches request)
Sequence: 0x00000001 (matches request)
MsgId:    0x0000000000000001 (matches request)
Code:     0x00 (OK)

Payload:
{
  "accountId": "...",
  "accessToken": "eyJ...",
  "refreshToken": "...",
  "expiresIn": 3600,
  "connectUrl": "wss://..."
}
```

### Error Response

```
Flags:    0x40 (Response)
Channel:  0x0000
Sequence: 0x00000001
MsgId:    0x0000000000000001
Code:     0x14 (20 = Unauthorized)

Payload: (empty)
```
