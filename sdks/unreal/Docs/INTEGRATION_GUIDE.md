# Bannou Unreal Engine Integration Guide

This guide explains how to integrate Bannou services into your Unreal Engine project using the generated helper files.

## Overview

The Bannou Unreal SDK provides header-only C++ helpers for:
- Binary WebSocket protocol implementation
- Type definitions for all API requests/responses
- Endpoint registry for routing
- Client event type constants

**Note**: This SDK provides types and protocol helpers only. You bring your own WebSocket implementation (IWebSocket, WebSocketsModule, or third-party).

## Prerequisites

- Unreal Engine 4.27+ or Unreal Engine 5.x
- WebSocket plugin enabled (Engine or third-party)
- C++ project (Blueprint-only projects need a C++ wrapper module)

## Quick Start

### 1. Copy Generated Files

Copy the `Generated/` directory to your project:

```
YourProject/
  Source/
    YourModule/
      Bannou/
        BannouProtocol.h
        BannouTypes.h
        BannouEnums.h
        BannouEndpoints.h
        BannouEvents.h
```

### 2. Update Build.cs

Add required modules to your `.Build.cs`:

```csharp
PublicDependencyModuleNames.AddRange(new string[]
{
    "Core",
    "CoreUObject",
    "Engine",
    "WebSockets",  // For IWebSocket
    "Json",
    "JsonUtilities"
});
```

### 3. Include Headers

```cpp
#include "Bannou/BannouProtocol.h"
#include "Bannou/BannouTypes.h"
#include "Bannou/BannouEndpoints.h"
```

## Authentication Flow

### HTTP Login

First, authenticate via HTTP to get a JWT token:

```cpp
// 1. Create login request
FLoginRequest LoginRequest;
LoginRequest.Email = TEXT("user@example.com");
LoginRequest.Password = TEXT("password123");

// 2. Serialize to JSON
FString JsonPayload;
FJsonObjectConverterModule::Get().UStructToJsonObjectString(
    FLoginRequest::StaticStruct(),
    &LoginRequest,
    JsonPayload
);

// 3. Send HTTP POST to /auth/login
// (Use your preferred HTTP library: FHttpModule, VaRest, etc.)

// 4. Parse response
FAuthResponse AuthResponse;
FJsonObjectConverterModule::Get().JsonObjectStringToUStruct(
    ResponseBody,
    &AuthResponse
);

// 5. Store tokens
FString AccessToken = AuthResponse.AccessToken;
FString ConnectUrl = AuthResponse.ConnectUrl;
```

### WebSocket Connection

Connect to the WebSocket endpoint with your JWT:

```cpp
// Create WebSocket with JWT in header
TSharedRef<IWebSocket> WebSocket = FWebSocketsModule::Get().CreateWebSocket(
    ConnectUrl,
    TEXT(""),  // Protocol
    TMap<FString, FString>{{TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken)}}
);

// Handle events
WebSocket->OnConnected().AddLambda([]()
{
    UE_LOG(LogTemp, Log, TEXT("Connected to Bannou"));
});

WebSocket->OnRawMessage().AddLambda([](const void* Data, SIZE_T Size, SIZE_T)
{
    // Parse binary message
    TArray<uint8> Buffer;
    Buffer.Append(static_cast<const uint8*>(Data), Size);

    Bannou::FBannouMessage Message;
    if (Bannou::ParseMessage(Buffer, Message))
    {
        // Handle message...
    }
});

WebSocket->Connect();
```

## Binary Protocol

### Message Structure

All messages use a binary header followed by a JSON payload:

**Request Header (31 bytes)**:
| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0 | 1 | Flags | Message behavior flags |
| 1 | 2 | Channel | Message channel (big-endian) |
| 3 | 4 | Sequence | Sequence number (big-endian) |
| 7 | 16 | ServiceGuid | Target service GUID (RFC 4122) |
| 23 | 8 | MessageId | Correlation ID (big-endian) |

**Response Header (16 bytes)**:
| Offset | Size | Field | Description |
|--------|------|-------|
| 0 | 1 | Flags | Response flag (0x40) set |
| 1 | 2 | Channel | Message channel |
| 3 | 4 | Sequence | Sequence number |
| 7 | 8 | MessageId | Correlation ID |
| 15 | 1 | ResponseCode | 0 = success, non-zero = error |

### Sending Requests

```cpp
// 1. Build request struct
FGetAccountRequest Request;
Request.AccountId = AccountGuid;

// 2. Serialize to JSON
FString JsonPayload;
FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

// 3. Get service GUID from capability manifest
FGuid ServiceGuid = CapabilityMap.FindRef(TEXT("POST:/account/get"));

// 4. Build binary message
Bannou::FBannouMessage Message;
Message.Flags = static_cast<uint8>(Bannou::EMessageFlags::None);
Message.Channel = 0;
Message.SequenceNumber = NextSequenceNumber++;
Message.ServiceGuid = ServiceGuid;
Message.MessageId = NextMessageId++;
Message.Payload = JsonPayload;

// 5. Serialize and send
TArray<uint8> Buffer;
Bannou::SerializeRequest(Message, Buffer);
WebSocket->Send(Buffer.GetData(), Buffer.Num(), true);  // true = binary
```

### Handling Responses

```cpp
void OnBinaryMessage(const void* Data, SIZE_T Size)
{
    TArray<uint8> Buffer;
    Buffer.Append(static_cast<const uint8*>(Data), Size);

    Bannou::FBannouMessage Message;
    if (!Bannou::ParseMessage(Buffer, Message))
    {
        UE_LOG(LogTemp, Error, TEXT("Failed to parse message"));
        return;
    }

    if (Message.IsResponseMessage())
    {
        // Find pending request by MessageId
        if (Message.IsSuccessResponse())
        {
            // Parse JSON response based on expected type
            FGetAccountResponse Response;
            FJsonObjectConverter::JsonObjectStringToUStruct(
                Message.Payload,
                &Response
            );
            // Handle success...
        }
        else
        {
            // Handle error
            Bannou::EResponseCode Code = static_cast<Bannou::EResponseCode>(Message.ResponseCode);
            UE_LOG(LogTemp, Error, TEXT("Request failed: %s"),
                   *Bannou::GetResponseCodeName(Code));
        }
    }
    else if (Bannou::IsEvent(Message.Flags))
    {
        // Handle server push event
        HandleEvent(Message);
    }
}
```

## Capability Manifest

After connecting, you'll receive a `CapabilityManifestEvent` with all available endpoints:

```cpp
void HandleCapabilityManifest(const FString& JsonPayload)
{
    FCapabilityManifestEvent Event;
    FJsonObjectConverter::JsonObjectStringToUStruct(JsonPayload, &Event);

    // Build endpoint -> GUID map
    CapabilityMap.Empty();
    for (const FClientCapabilityEntry& Cap : Event.AvailableApis)
    {
        FString Key = FString::Printf(TEXT("%s:%s"), *Cap.Method, *Cap.Path);
        FGuid Guid;
        FGuid::Parse(Cap.ServiceId, Guid);
        CapabilityMap.Add(Key, Guid);
    }

    UE_LOG(LogTemp, Log, TEXT("Received %d capabilities"), Event.AvailableApis.Num());
}
```

## Server Push Events

Handle events pushed from the server:

```cpp
void HandleEvent(const Bannou::FBannouMessage& Message)
{
    // Parse base event to get event name
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Message.Payload);
    if (!FJsonSerializer::Deserialize(Reader, JsonObject))
    {
        return;
    }

    FString EventName = JsonObject->GetStringField(TEXT("eventName"));

    if (EventName == Bannou::Events::CapabilityManifest)
    {
        HandleCapabilityManifest(Message.Payload);
    }
    else if (EventName == Bannou::Events::DisconnectNotification)
    {
        HandleDisconnect(Message.Payload);
    }
    // Add more event handlers...
}
```

## Error Handling

### Response Codes

```cpp
void HandleError(uint8 ResponseCode)
{
    Bannou::EResponseCode Code = static_cast<Bannou::EResponseCode>(ResponseCode);

    switch (Code)
    {
    case Bannou::EResponseCode::Unauthorized:
        // Re-authenticate
        break;

    case Bannou::EResponseCode::ServiceNotFound:
        // Refresh capability manifest
        break;

    case Bannou::EResponseCode::TooManyRequests:
        // Back off and retry
        break;

    default:
        UE_LOG(LogTemp, Error, TEXT("Error: %s (HTTP %d)"),
               *Bannou::GetResponseCodeName(Code),
               Bannou::MapToHttpStatus(Code));
    }
}
```

### Reconnection

On unexpected disconnect:

```cpp
void OnDisconnected()
{
    // Check for reconnection token from DisconnectNotificationEvent
    if (!ReconnectionToken.IsEmpty())
    {
        // Reconnect within 5 minutes using token
        // Token restores session state without re-authentication
    }
    else
    {
        // Full re-authentication required
    }
}
```

## Type Mapping Reference

| OpenAPI Type | Unreal C++ Type |
|--------------|-----------------|
| string | FString |
| string (uuid) | FGuid |
| string (date-time) | FDateTime |
| integer | int32 |
| integer (int64) | int64 |
| number | float |
| number (double) | double |
| boolean | bool |
| array | TArray<T> |
| nullable T | TOptional<T> |
| object | TMap<FString, FString> |

## Best Practices

1. **Message Correlation**: Store pending requests in a TMap keyed by MessageId for response matching.

2. **Channel Usage**: Use channel 0 for general requests. Use separate channels for ordered message streams (e.g., game state updates).

3. **Sequence Numbers**: Track per-channel sequence numbers for message ordering and duplicate detection.

4. **Blueprint Exposure**: All generated structs use USTRUCT(BlueprintType) for Blueprint access.

5. **Memory Management**: Use TSharedPtr/TSharedRef for WebSocket instances to ensure proper cleanup.

## Regenerating Headers

When Bannou schemas change:

```bash
# From repository root
make generate-unreal-sdk

# Or manually
python3 scripts/generate-unreal-sdk.py
```

## Further Reading

- [PROTOCOL_REFERENCE.md](PROTOCOL_REFERENCE.md) - Detailed binary protocol specification
- [EXAMPLES.md](EXAMPLES.md) - Complete code examples
- [Bannou Architecture](../../../docs/BANNOU_DESIGN.md) - System design overview
