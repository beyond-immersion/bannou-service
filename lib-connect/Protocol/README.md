# Bannou Connect Protocol Classes

## Overview

This directory contains the core binary protocol classes for Bannou's WebSocket-first Connect service. These classes are designed to be **dependency-free** and can be extracted for use in Client SDKs without requiring any ASP.NET Core or server-side dependencies.

## Protocol Classes

### Core Message Structure
- **`BinaryMessage.cs`** - Complete binary message with 31-byte header and payload
- **`MessageFlags.cs`** - Bit flags controlling message behavior (binary, encrypted, compressed, etc.)
- **`ResponseCodes.cs`** - Response codes for success/error indication

### Connection Management
- **`ConnectionState.cs`** - WebSocket connection state and service mappings
- **`MessageRouter.cs`** - Dependency-free message routing logic

### Security & Utilities
- **`GuidGenerator.cs`** - Client-salted GUID generation for security isolation

## Client SDK Extraction

These classes can be copied directly to Client SDKs with the following characteristics:

### Zero External Dependencies
- Only uses `System.*` namespaces (System.Text, System.Security.Cryptography, etc.)
- No ASP.NET Core dependencies
- No external infrastructure dependencies
- No logging framework dependencies

### Namespace Structure
```csharp
namespace BeyondImmersion.BannouService.Connect.Protocol;
```

For Client SDKs, you can rename the namespace to match your client architecture:
```csharp
namespace YourClientApp.Bannou.Protocol;
```

## Usage Examples

### Creating a Binary Message
```csharp
var message = BinaryMessage.FromJson(
    channel: 0,
    sequenceNumber: 1,
    serviceGuid: serviceGuid,
    messageId: GuidGenerator.GenerateMessageId(),
    jsonPayload: "{\"action\":\"test\"}"
);

byte[] messageBytes = message.ToByteArray();
```

### Parsing Received Messages
```csharp
var receivedMessage = BinaryMessage.Parse(buffer, messageLength);
string jsonPayload = receivedMessage.GetJsonPayload();
```

### Generating Secure Service GUIDs
```csharp
var serviceGuid = GuidGenerator.GenerateServiceGuid(
    sessionId: "user-session-123",
    serviceName: "accounts",
    serverSalt: "server-provided-salt"
);
```

### Message Routing Analysis
```csharp
var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);
if (routeInfo.IsValid)
{
    // Route to service or client
}
else
{
    var errorResponse = MessageRouter.CreateErrorResponse(
        message, routeInfo.ErrorCode, routeInfo.ErrorMessage);
}
```

## Protocol Specifications

### Binary Message Format (31-byte header)
```
[Message Flags: 1 byte]    - MessageFlags enum (native byte order)
[Channel: 2 bytes]         - Channel for sequential processing (NETWORK BYTE ORDER - big-endian)
[Sequence: 4 bytes]        - Per-channel sequence number (NETWORK BYTE ORDER - big-endian)
[Service GUID: 16 bytes]   - Client-salted service identifier (RFC 4122 network order)
[Message ID: 8 bytes]      - Unique message ID for correlation (NETWORK BYTE ORDER - big-endian)
[Payload: variable]        - JSON or binary payload
```

**⚠️ CRITICAL: Endianness Requirements**

All multi-byte fields use **Network Byte Order (big-endian)** for cross-platform compatibility:

- **Channel (2 bytes)**: Always big-endian regardless of system architecture
- **Sequence Number (4 bytes)**: Always big-endian regardless of system architecture
- **Service GUID (16 bytes)**: RFC 4122 network standard byte ordering
- **Message ID (8 bytes)**: Always big-endian regardless of system architecture

This ensures the protocol works identically across:
- x86/x64 servers (little-endian)
- ARM mobile clients (mixed endianness)
- WebAssembly clients (configurable endianness)
- Unity game clients (platform-dependent endianness)

### Security Model
- **Client-Salted GUIDs**: Each client gets unique GUIDs for identical services
- **Session Isolation**: GUIDs prevent cross-session exploitation
- **Cryptographic Hashing**: SHA256-based GUID generation with server salt

### Channel System
- **Channel 0**: Default channel for unordered messages
- **Channel 1**: Authentication operations (sequential)
- **Channel 2**: Game session operations (sequential)
- **Channel 3**: Behavior operations (sequential)
- **Channel 4**: Permission operations (sequential)

### Message Flags
- **0x01**: Binary payload (instead of JSON)
- **0x02**: Encrypted payload
- **0x04**: Compressed payload (gzip)
- **0x08**: High priority (skip to front of queues)
- **0x10**: Event (fire-and-forget, no response expected)
- **0x20**: Client-to-client routing
- **0x40**: Response message (not a new request)
- **0x80**: Reserved for future use

## Testing & Validation

The protocol classes include extensive validation methods:

```csharp
// Validate GUID authenticity
bool isValid = GuidGenerator.ValidateServiceGuid(
    guid, sessionId, serviceName, serverSalt);

// Check rate limiting
var rateLimitResult = MessageRouter.CheckRateLimit(connectionState);

// Cross-platform compatibility validation
bool isCompatible = BinaryProtocolTests.RunCrossPlatformCompatibilityTests();
```

### Cross-Platform Validation

**CRITICAL**: Always run cross-platform compatibility tests when integrating into new environments:

```csharp
// Test network byte order support
if (!NetworkByteOrder.IsNetworkByteOrderSupported())
{
    throw new NotSupportedException("Network byte order operations not supported");
}

// Validate protocol compatibility
if (!NetworkByteOrder.TestNetworkByteOrderCompatibility())
{
    throw new InvalidOperationException("Network byte order compatibility test failed");
}

// Run comprehensive protocol tests
if (!BinaryProtocolTests.RunCrossPlatformCompatibilityTests())
{
    throw new InvalidOperationException("Binary protocol cross-platform tests failed");
}
```

The test suite validates:
- ✅ Network byte order operations on current system
- ✅ Binary message serialization/deserialization round-trips
- ✅ GUID network order consistency across platforms
- ✅ Edge cases (empty payloads, maximum values)
- ✅ Real-world message scenarios (auth, physics, responses)
- ✅ Simulated cross-endianness compatibility

## Performance Considerations

### Zero-Copy Design
- Binary parsing avoids unnecessary memory allocations
- `ReadOnlyMemory<byte>` for payload handling
- Efficient header parsing with `BitConverter`

### Memory Efficiency
- Struct-based `BinaryMessage` for minimal heap allocation
- Connection state uses `ConcurrentDictionary` for thread safety
- Automatic cleanup of expired pending messages

## Client SDK Integration Notes

When integrating these classes into Client SDKs:

1. **Copy all .cs files** from this directory
2. **Update namespaces** to match your client architecture
3. **Add any client-specific extensions** while maintaining core compatibility
4. **Test binary protocol compatibility** with the server implementation
5. **Maintain the same message format** for server compatibility

The classes are designed to work identically on both client and server sides, ensuring consistent binary protocol behavior across all implementations.
