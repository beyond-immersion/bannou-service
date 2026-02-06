# Unreal Engine Integration Guide

This guide covers integrating Bannou services into Unreal Engine 4 and 5 projects using the generated helper artifacts.

## Overview

Bannou provides **helper artifacts** for Unreal Engine integration rather than a full SDK. This approach gives game developers full control over their networking implementation while providing:

- **Generated C++ Headers**: Type definitions for all request/response models
- **Protocol Constants**: Binary protocol specifications for WebSocket communication
- **Endpoint Registry**: All 309 client-accessible endpoints with metadata
- **Event Definitions**: All 35 server-push event types
- **Comprehensive Documentation**: Integration guides and code examples

## Generated Artifacts

The following files are generated in `sdks/unreal/Generated/`:

| File | Purpose |
|------|---------|
| `BannouProtocol.h` | Binary protocol constants, message flags, response codes |
| `BannouTypes.h` | All 814 request/response structs with USTRUCT macros |
| `BannouEnums.h` | All enum types with UENUM macros |
| `BannouEndpoints.h` | Endpoint constants and metadata registry |
| `BannouEvents.h` | Event name constants for server-push events |

## Quick Start

### 1. Copy Generated Headers

Copy the generated headers to your Unreal project:

```bash
cp sdks/unreal/Generated/*.h YourProject/Source/YourProject/Bannou/
```

### 2. Include in Your Module

In your module's `Build.cs`:

```csharp
PublicDependencyModuleNames.AddRange(new string[] {
    "Core",
    "CoreUObject",
    "Engine",
    "Json",
    "JsonUtilities",
    "WebSockets"  // For WebSocket support
});
```

### 3. Include Headers

```cpp
#include "Bannou/BannouProtocol.h"
#include "Bannou/BannouTypes.h"
#include "Bannou/BannouEndpoints.h"
#include "Bannou/BannouEvents.h"
```

## Binary Protocol

Bannou uses a binary WebSocket protocol with JSON payloads. You must implement the binary message framing yourself.

### Request Header (31 bytes)

```cpp
// From BannouProtocol.h
constexpr int32 REQUEST_HEADER_SIZE = 31;

// Header layout:
// [0]     Flags (1 byte)      - Message flags
// [1-2]   Channel (2 bytes)   - Message channel (big-endian)
// [3-6]   Sequence (4 bytes)  - Sequence number (big-endian)
// [7-22]  GUID (16 bytes)     - Service endpoint GUID (RFC 4122)
// [23-30] MessageId (8 bytes) - Correlation ID (big-endian)
```

### Response Header (16 bytes)

```cpp
constexpr int32 RESPONSE_HEADER_SIZE = 16;

// Header layout:
// [0]     Flags (1 byte)        - Response flags
// [1]     ResponseCode (1 byte) - Status code (0 = OK)
// [2-3]   Reserved (2 bytes)    - Reserved for future use
// [4-7]   PayloadLen (4 bytes)  - JSON payload length (big-endian)
// [8-15]  MessageId (8 bytes)   - Correlation ID from request (big-endian)
```

### Message Flags

```cpp
namespace Bannou
{
    enum class EMessageFlags : uint8
    {
        None        = 0x00,
        Binary      = 0x01,  // Payload is binary (not JSON)
        Encrypted   = 0x02,  // Payload is encrypted
        Compressed  = 0x04,  // Payload is compressed
        HighPriority= 0x08,  // High priority message
        Event       = 0x10,  // Server-push event
        Client      = 0x20,  // Client-originated message
        Response    = 0x40,  // Response to request
        Meta        = 0x80   // Metadata/control message
    };
}
```

### Response Codes

```cpp
namespace Bannou
{
    enum class EResponseCode : uint8
    {
        OK = 0,                  // Success
        RequestError = 10,       // Malformed request
        Unauthorized = 20,       // Authentication required
        Forbidden = 21,          // Access denied
        ServiceNotFound = 30,    // Unknown service GUID
        MethodNotAllowed = 31,   // HTTP method not allowed
        Timeout = 40,            // Request timed out
        ServerError = 50,        // Internal server error
        ServiceUnavailable = 51, // Service temporarily unavailable
    };
}
```

## Using Generated Types

### Request/Response Structs

All types are generated as USTRUCTs with JSON serialization support:

```cpp
#include "Bannou/BannouTypes.h"

// Create a login request
Bannou::FLoginRequest LoginRequest;
LoginRequest.Email = TEXT("player@example.com");
LoginRequest.Password = TEXT("password");

// Serialize to JSON
FString JsonString;
FJsonObjectConverter::UStructToJsonObjectString(LoginRequest, JsonString);

// Parse a response
Bannou::FLoginResponse LoginResponse;
FJsonObjectConverter::JsonObjectStringToUStruct(ResponseJson, &LoginResponse);

// Access response data
UE_LOG(LogTemp, Log, TEXT("Account ID: %s"), *LoginResponse.AccountId);
UE_LOG(LogTemp, Log, TEXT("Access Token: %s"), *LoginResponse.AccessToken);
```

### Enum Types

```cpp
#include "Bannou/BannouEnums.h"

// Use generated enums
Bannou::EGameSessionState SessionState = Bannou::EGameSessionState::Active;

// Enum to string (for JSON serialization)
FString StateString = StaticEnum<Bannou::EGameSessionState>()->GetNameStringByValue(
    static_cast<int64>(SessionState)
);
```

### Endpoint Metadata

```cpp
#include "Bannou/BannouEndpoints.h"

// Access endpoint constants
const TCHAR* LoginEndpoint = Bannou::Endpoints::AuthLogin;
// Value: "POST:/auth/login"

// Or use the registry
const Bannou::FEndpointInfo* Info = Bannou::Endpoints::GetEndpointInfo(TEXT("auth/login"));
if (Info)
{
    UE_LOG(LogTemp, Log, TEXT("Method: %s, Path: %s"), *Info->Method, *Info->Path);
    UE_LOG(LogTemp, Log, TEXT("Request: %s, Response: %s"), *Info->RequestType, *Info->ResponseType);
}
```

### Event Names

```cpp
#include "Bannou/BannouEvents.h"

// Subscribe to events by name
const TCHAR* ChatEventName = Bannou::Events::GameSessionChatReceived;
// Value: "game_session.chat_received"

const TCHAR* MatchFoundEventName = Bannou::Events::MatchmakingMatchFound;
// Value: "matchmaking.match_found"
```

## Connection Manager Example

A basic connection manager implementation:

```cpp
// BannouConnectionManager.h
#pragma once

#include "CoreMinimal.h"
#include "IWebSocket.h"
#include "Bannou/BannouProtocol.h"

DECLARE_MULTICAST_DELEGATE_TwoParams(FOnBannouResponse, uint64, const FString&);
DECLARE_MULTICAST_DELEGATE_TwoParams(FOnBannouEvent, const FString&, const FString&);

class YOURGAME_API FBannouConnectionManager
{
public:
    void Connect(const FString& Url, const FString& AccessToken);
    void Disconnect();

    // Send request, returns correlation ID
    uint64 SendRequest(const FGuid& ServiceGuid, const FString& JsonPayload);

    FOnBannouResponse OnResponse;
    FOnBannouEvent OnEvent;

private:
    TSharedPtr<IWebSocket> WebSocket;
    uint64 NextMessageId = 1;
    uint16 CurrentSequence = 0;

    void OnMessage(const FString& Message);
    void OnBinaryMessage(const void* Data, SIZE_T Size, bool bIsLastFragment);

    // Build request header
    TArray<uint8> BuildRequestHeader(const FGuid& ServiceGuid, uint64 MessageId);
};
```

```cpp
// BannouConnectionManager.cpp
#include "BannouConnectionManager.h"
#include "WebSocketsModule.h"

void FBannouConnectionManager::Connect(const FString& Url, const FString& AccessToken)
{
    FWebSocketsModule& Module = FModuleManager::LoadModuleChecked<FWebSocketsModule>("WebSockets");

    // Add auth header
    TMap<FString, FString> Headers;
    Headers.Add(TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken));

    WebSocket = Module.CreateWebSocket(Url, TEXT(""), Headers);

    WebSocket->OnMessage().AddRaw(this, &FBannouConnectionManager::OnMessage);
    WebSocket->OnBinaryMessage().AddRaw(this, &FBannouConnectionManager::OnBinaryMessage);

    WebSocket->Connect();
}

uint64 FBannouConnectionManager::SendRequest(const FGuid& ServiceGuid, const FString& JsonPayload)
{
    uint64 MessageId = NextMessageId++;

    // Build binary header
    TArray<uint8> Header = BuildRequestHeader(ServiceGuid, MessageId);

    // Convert JSON to UTF-8
    FTCHARToUTF8 Converter(*JsonPayload);
    TArray<uint8> Payload;
    Payload.Append((const uint8*)Converter.Get(), Converter.Length());

    // Combine header + payload
    TArray<uint8> Message;
    Message.Append(Header);
    Message.Append(Payload);

    WebSocket->Send(Message.GetData(), Message.Num(), true);

    return MessageId;
}

TArray<uint8> FBannouConnectionManager::BuildRequestHeader(const FGuid& ServiceGuid, uint64 MessageId)
{
    TArray<uint8> Header;
    Header.SetNumZeroed(Bannou::REQUEST_HEADER_SIZE);

    // Flags (Client flag set)
    Header[0] = static_cast<uint8>(Bannou::EMessageFlags::Client);

    // Channel (big-endian, default 0)
    Bannou::NetworkByteOrder::WriteUInt16(Header.GetData() + 1, 0);

    // Sequence (big-endian)
    Bannou::NetworkByteOrder::WriteUInt32(Header.GetData() + 3, CurrentSequence++);

    // GUID (RFC 4122 format)
    Bannou::NetworkByteOrder::WriteGuid(Header.GetData() + 7, ServiceGuid);

    // Message ID (big-endian)
    Bannou::NetworkByteOrder::WriteUInt64(Header.GetData() + 23, MessageId);

    return Header;
}

void FBannouConnectionManager::OnBinaryMessage(const void* Data, SIZE_T Size, bool bIsLastFragment)
{
    if (Size < Bannou::RESPONSE_HEADER_SIZE)
    {
        return; // Invalid message
    }

    const uint8* Bytes = static_cast<const uint8*>(Data);

    // Parse header
    uint8 Flags = Bytes[0];
    uint8 ResponseCode = Bytes[1];
    uint32 PayloadLen = Bannou::NetworkByteOrder::ReadUInt32(Bytes + 4);
    uint64 MessageId = Bannou::NetworkByteOrder::ReadUInt64(Bytes + 8);

    // Extract JSON payload
    FString JsonPayload;
    if (PayloadLen > 0 && Size >= Bannou::RESPONSE_HEADER_SIZE + PayloadLen)
    {
        FUTF8ToTCHAR Converter(
            reinterpret_cast<const ANSICHAR*>(Bytes + Bannou::RESPONSE_HEADER_SIZE),
            PayloadLen
        );
        JsonPayload = FString(Converter.Length(), Converter.Get());
    }

    // Check if it's an event
    if (Flags & static_cast<uint8>(Bannou::EMessageFlags::Event))
    {
        // Parse event name from JSON
        TSharedPtr<FJsonObject> JsonObject;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonPayload);
        if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
        {
            FString EventName = JsonObject->GetStringField(TEXT("event"));
            OnEvent.Broadcast(EventName, JsonPayload);
        }
    }
    else
    {
        OnResponse.Broadcast(MessageId, JsonPayload);
    }
}
```

## Detailed Documentation

For comprehensive implementation details, see the generated documentation in `sdks/unreal/Docs/`:

- **[Integration Guide](../../sdks/unreal/Docs/INTEGRATION_GUIDE.md)** - Complete integration walkthrough
- **[Protocol Reference](../../sdks/unreal/Docs/PROTOCOL_REFERENCE.md)** - Detailed binary protocol specification
- **[Examples](../../sdks/unreal/Docs/EXAMPLES.md)** - Full code examples

## Regenerating Headers

When the Bannou schemas change, regenerate the headers:

```bash
make generate-unreal-sdk
```

This runs:
1. `generate-client-schema.py` - Creates consolidated OpenAPI schema
2. `generate-unreal-sdk.py` - Generates all C++ headers

## Troubleshooting

### Byte Order Issues

All multi-byte integers in the protocol use **big-endian** (network) byte order. Ensure you're using the provided `NetworkByteOrder` utilities or equivalent.

### GUID Serialization

GUIDs must be serialized in **RFC 4122** format (network byte order for time fields):

```cpp
// RFC 4122 GUID byte order:
// Bytes 0-3:   time_low (big-endian)
// Bytes 4-5:   time_mid (big-endian)
// Bytes 6-7:   time_hi_and_version (big-endian)
// Bytes 8-9:   clock_seq (as-is)
// Bytes 10-15: node (as-is)
```

### Message Correlation

Always track the `MessageId` returned from `SendRequest()` to correlate responses with requests. Consider using a `TMap<uint64, TPromise<FString>>` or similar pattern.

### Capability Manifest

After connecting, the server sends a `connect.capability_manifest` event containing the available endpoints and their GUIDs. You must parse this to get the service GUIDs for your requests.

## Related Documentation

- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Full protocol specification
- [SDKs Overview](SDK-OVERVIEW.md) - All Bannou SDKs
- [Client Integration](CLIENT-INTEGRATION.md) - General client integration patterns
