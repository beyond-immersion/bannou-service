# Bannou Unreal Engine Code Examples

This document provides complete code examples for common Bannou integration scenarios.

## Table of Contents

1. [Basic Connection Manager](#basic-connection-manager)
2. [Request/Response Handling](#requestresponse-handling)
3. [Event Subscription](#event-subscription)
4. [Game Session Example](#game-session-example)

## Basic Connection Manager

A complete connection manager class for handling WebSocket communication:

```cpp
// BannouConnection.h

#pragma once

#include "CoreMinimal.h"
#include "IWebSocket.h"
#include "Bannou/BannouProtocol.h"
#include "Bannou/BannouTypes.h"

DECLARE_DYNAMIC_MULTICAST_DELEGATE(FOnBannouConnected);
DECLARE_DYNAMIC_MULTICAST_DELEGATE(FOnBannouDisconnected);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnBannouError, FString, ErrorMessage);

UCLASS(BlueprintType)
class YOURGAME_API UBannouConnection : public UObject
{
    GENERATED_BODY()

public:
    UBannouConnection();

    // Connection management
    UFUNCTION(BlueprintCallable, Category = "Bannou")
    void Connect(const FString& Url, const FString& AccessToken);

    UFUNCTION(BlueprintCallable, Category = "Bannou")
    void Disconnect();

    UFUNCTION(BlueprintPure, Category = "Bannou")
    bool IsConnected() const;

    // Send request with callback
    void SendRequest(
        const FGuid& ServiceGuid,
        const FString& JsonPayload,
        TFunction<void(bool, const FString&)> Callback
    );

    // Events
    UPROPERTY(BlueprintAssignable, Category = "Bannou")
    FOnBannouConnected OnConnected;

    UPROPERTY(BlueprintAssignable, Category = "Bannou")
    FOnBannouDisconnected OnDisconnected;

    UPROPERTY(BlueprintAssignable, Category = "Bannou")
    FOnBannouError OnError;

    // Capability lookup
    UFUNCTION(BlueprintPure, Category = "Bannou")
    FGuid GetServiceGuid(const FString& EndpointKey) const;

protected:
    TSharedPtr<IWebSocket> WebSocket;

    // Request tracking
    TMap<int64, TFunction<void(bool, const FString&)>> PendingRequests;
    int64 NextMessageId = 1;
    int32 NextSequence = 0;

    // Capability manifest
    TMap<FString, FGuid> CapabilityMap;

    void OnWebSocketConnected();
    void OnWebSocketConnectionError(const FString& Error);
    void OnWebSocketClosed(int32 StatusCode, const FString& Reason, bool bWasClean);
    void OnWebSocketMessage(const void* Data, SIZE_T Size, SIZE_T BytesRemaining);

    void HandleCapabilityManifest(const FString& JsonPayload);
    void HandleResponse(const Bannou::FBannouMessage& Message);
    void HandleEvent(const Bannou::FBannouMessage& Message);
};
```

```cpp
// BannouConnection.cpp

#include "BannouConnection.h"
#include "WebSocketsModule.h"
#include "JsonObjectConverter.h"

UBannouConnection::UBannouConnection()
{
}

void UBannouConnection::Connect(const FString& Url, const FString& AccessToken)
{
    if (WebSocket.IsValid() && WebSocket->IsConnected())
    {
        Disconnect();
    }

    // Create WebSocket with auth header
    TMap<FString, FString> Headers;
    Headers.Add(TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken));

    WebSocket = FWebSocketsModule::Get().CreateWebSocket(Url, TEXT(""), Headers);

    WebSocket->OnConnected().AddUObject(this, &UBannouConnection::OnWebSocketConnected);
    WebSocket->OnConnectionError().AddUObject(this, &UBannouConnection::OnWebSocketConnectionError);
    WebSocket->OnClosed().AddUObject(this, &UBannouConnection::OnWebSocketClosed);
    WebSocket->OnRawMessage().AddLambda([this](const void* Data, SIZE_T Size, SIZE_T Remaining)
    {
        OnWebSocketMessage(Data, Size, Remaining);
    });

    WebSocket->Connect();
}

void UBannouConnection::Disconnect()
{
    if (WebSocket.IsValid())
    {
        WebSocket->Close();
        WebSocket.Reset();
    }
    PendingRequests.Empty();
    CapabilityMap.Empty();
}

bool UBannouConnection::IsConnected() const
{
    return WebSocket.IsValid() && WebSocket->IsConnected();
}

void UBannouConnection::SendRequest(
    const FGuid& ServiceGuid,
    const FString& JsonPayload,
    TFunction<void(bool, const FString&)> Callback)
{
    if (!IsConnected())
    {
        if (Callback)
        {
            Callback(false, TEXT("Not connected"));
        }
        return;
    }

    int64 MessageId = NextMessageId++;
    int32 Sequence = NextSequence++;

    // Build message
    Bannou::FBannouMessage Message;
    Message.Flags = static_cast<uint8>(Bannou::EMessageFlags::None);
    Message.Channel = 0;
    Message.SequenceNumber = Sequence;
    Message.ServiceGuid = ServiceGuid;
    Message.MessageId = MessageId;
    Message.Payload = JsonPayload;

    // Track callback
    if (Callback)
    {
        PendingRequests.Add(MessageId, Callback);
    }

    // Serialize and send
    TArray<uint8> Buffer;
    Bannou::SerializeRequest(Message, Buffer);
    WebSocket->Send(Buffer.GetData(), Buffer.Num(), true);
}

FGuid UBannouConnection::GetServiceGuid(const FString& EndpointKey) const
{
    const FGuid* Found = CapabilityMap.Find(EndpointKey);
    return Found ? *Found : Bannou::EMPTY_GUID;
}

void UBannouConnection::OnWebSocketConnected()
{
    UE_LOG(LogTemp, Log, TEXT("Bannou: Connected"));
    OnConnected.Broadcast();
}

void UBannouConnection::OnWebSocketConnectionError(const FString& Error)
{
    UE_LOG(LogTemp, Error, TEXT("Bannou: Connection error: %s"), *Error);
    OnError.Broadcast(Error);
}

void UBannouConnection::OnWebSocketClosed(int32 StatusCode, const FString& Reason, bool bWasClean)
{
    UE_LOG(LogTemp, Log, TEXT("Bannou: Disconnected (code=%d, reason=%s)"), StatusCode, *Reason);

    // Fail all pending requests
    for (auto& Pair : PendingRequests)
    {
        Pair.Value(false, TEXT("Connection closed"));
    }
    PendingRequests.Empty();

    OnDisconnected.Broadcast();
}

void UBannouConnection::OnWebSocketMessage(const void* Data, SIZE_T Size, SIZE_T BytesRemaining)
{
    TArray<uint8> Buffer;
    Buffer.Append(static_cast<const uint8*>(Data), Size);

    Bannou::FBannouMessage Message;
    if (!Bannou::ParseMessage(Buffer, Message))
    {
        UE_LOG(LogTemp, Error, TEXT("Bannou: Failed to parse message"));
        return;
    }

    if (Message.IsResponseMessage())
    {
        HandleResponse(Message);
    }
    else if (Bannou::IsEvent(Message.Flags))
    {
        HandleEvent(Message);
    }
}

void UBannouConnection::HandleCapabilityManifest(const FString& JsonPayload)
{
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonPayload);
    if (!FJsonSerializer::Deserialize(Reader, JsonObject))
    {
        return;
    }

    const TArray<TSharedPtr<FJsonValue>>* Apis;
    if (JsonObject->TryGetArrayField(TEXT("availableApis"), Apis))
    {
        CapabilityMap.Empty();

        for (const auto& ApiValue : *Apis)
        {
            const TSharedPtr<FJsonObject>* ApiObj;
            if (ApiValue->TryGetObject(ApiObj))
            {
                FString Method = (*ApiObj)->GetStringField(TEXT("method"));
                FString Path = (*ApiObj)->GetStringField(TEXT("path"));
                FString ServiceId = (*ApiObj)->GetStringField(TEXT("serviceId"));

                FString Key = FString::Printf(TEXT("%s:%s"), *Method, *Path);
                FGuid Guid;
                FGuid::Parse(ServiceId, Guid);
                CapabilityMap.Add(Key, Guid);
            }
        }

        UE_LOG(LogTemp, Log, TEXT("Bannou: Loaded %d capabilities"), CapabilityMap.Num());
    }
}

void UBannouConnection::HandleResponse(const Bannou::FBannouMessage& Message)
{
    TFunction<void(bool, const FString&)>* Callback = PendingRequests.Find(Message.MessageId);
    if (Callback)
    {
        if (Message.IsSuccessResponse())
        {
            (*Callback)(true, Message.Payload);
        }
        else
        {
            Bannou::EResponseCode Code = static_cast<Bannou::EResponseCode>(Message.ResponseCode);
            FString ErrorMsg = FString::Printf(TEXT("Error %d: %s"),
                Message.ResponseCode,
                *Bannou::GetResponseCodeName(Code));
            (*Callback)(false, ErrorMsg);
        }
        PendingRequests.Remove(Message.MessageId);
    }
}

void UBannouConnection::HandleEvent(const Bannou::FBannouMessage& Message)
{
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Message.Payload);
    if (!FJsonSerializer::Deserialize(Reader, JsonObject))
    {
        return;
    }

    FString EventName = JsonObject->GetStringField(TEXT("eventName"));

    if (EventName == TEXT("connect.capability_manifest"))
    {
        HandleCapabilityManifest(Message.Payload);
    }
    else if (EventName == TEXT("connect.disconnect_notification"))
    {
        // Handle disconnect notification
        FString Reason = JsonObject->GetStringField(TEXT("reason"));
        UE_LOG(LogTemp, Warning, TEXT("Bannou: Disconnect notification: %s"), *Reason);
    }
    // Add more event handlers as needed
}
```

## Request/Response Handling

Type-safe request handling with automatic serialization:

```cpp
// BannouRequests.h

#pragma once

#include "BannouConnection.h"
#include "Bannou/BannouTypes.h"

/**
 * Helper class for type-safe Bannou API requests.
 */
UCLASS()
class UBannouRequests : public UObject
{
    GENERATED_BODY()

public:
    // Set the connection to use
    void SetConnection(UBannouConnection* InConnection)
    {
        Connection = InConnection;
    }

    // Type-safe login request
    void Login(
        const FLoginRequest& Request,
        TFunction<void(bool, const FAuthResponse&)> Callback)
    {
        FString JsonPayload;
        FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

        FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/auth/login"));

        Connection->SendRequest(ServiceGuid, JsonPayload,
            [Callback](bool bSuccess, const FString& Response)
            {
                FAuthResponse AuthResponse;
                if (bSuccess)
                {
                    FJsonObjectConverter::JsonObjectStringToUStruct(Response, &AuthResponse);
                }
                Callback(bSuccess, AuthResponse);
            });
    }

    // Type-safe account retrieval
    void GetAccount(
        const FGuid& AccountId,
        TFunction<void(bool, const FAccountModel&)> Callback)
    {
        FGetAccountRequest Request;
        Request.AccountId = AccountId;

        FString JsonPayload;
        FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

        FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/account/get"));

        Connection->SendRequest(ServiceGuid, JsonPayload,
            [Callback](bool bSuccess, const FString& Response)
            {
                FAccountModel Account;
                if (bSuccess)
                {
                    FJsonObjectConverter::JsonObjectStringToUStruct(Response, &Account);
                }
                Callback(bSuccess, Account);
            });
    }

    // Generic request method
    template<typename TRequest, typename TResponse>
    void Request(
        const FString& EndpointKey,
        const TRequest& RequestData,
        TFunction<void(bool, const TResponse&)> Callback)
    {
        FString JsonPayload;
        FJsonObjectConverter::UStructToJsonObjectString(RequestData, JsonPayload);

        FGuid ServiceGuid = Connection->GetServiceGuid(EndpointKey);

        Connection->SendRequest(ServiceGuid, JsonPayload,
            [Callback](bool bSuccess, const FString& Response)
            {
                TResponse ResponseData;
                if (bSuccess)
                {
                    FJsonObjectConverter::JsonObjectStringToUStruct(Response, &ResponseData);
                }
                Callback(bSuccess, ResponseData);
            });
    }

protected:
    UPROPERTY()
    UBannouConnection* Connection;
};
```

## Event Subscription

Handling server-pushed events:

```cpp
// BannouEventHandler.h

#pragma once

#include "CoreMinimal.h"
#include "Bannou/BannouEvents.h"

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnCapabilityUpdate, int32, CapabilityCount);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnGameStateUpdate, const FString&, StateJson);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnChatMessage, const FString&, Sender, const FString&, Message);

UCLASS()
class UBannouEventHandler : public UObject
{
    GENERATED_BODY()

public:
    // Event delegates
    UPROPERTY(BlueprintAssignable, Category = "Bannou|Events")
    FOnCapabilityUpdate OnCapabilityUpdate;

    UPROPERTY(BlueprintAssignable, Category = "Bannou|Events")
    FOnGameStateUpdate OnGameStateUpdate;

    UPROPERTY(BlueprintAssignable, Category = "Bannou|Events")
    FOnChatMessage OnChatMessage;

    // Process incoming event
    void HandleEvent(const FString& EventName, const TSharedPtr<FJsonObject>& EventData)
    {
        if (EventName == Bannou::Events::CapabilityManifest)
        {
            const TArray<TSharedPtr<FJsonValue>>* Apis;
            if (EventData->TryGetArrayField(TEXT("availableApis"), Apis))
            {
                OnCapabilityUpdate.Broadcast(Apis->Num());
            }
        }
        else if (EventName == TEXT("game-session.state_updated"))
        {
            FString StateJson;
            auto Writer = TJsonWriterFactory<>::Create(&StateJson);
            FJsonSerializer::Serialize(EventData.ToSharedRef(), Writer);
            OnGameStateUpdate.Broadcast(StateJson);
        }
        else if (EventName == TEXT("game-session.chat_message"))
        {
            FString Sender = EventData->GetStringField(TEXT("senderName"));
            FString Message = EventData->GetStringField(TEXT("message"));
            OnChatMessage.Broadcast(Sender, Message);
        }
    }
};
```

## Game Session Example

Complete example of joining and interacting with a game session:

```cpp
// GameSessionManager.h

#pragma once

#include "CoreMinimal.h"
#include "BannouConnection.h"
#include "Bannou/BannouTypes.h"

UCLASS(BlueprintType)
class UGameSessionManager : public UObject
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "Game")
    void Initialize(UBannouConnection* InConnection);

    UFUNCTION(BlueprintCallable, Category = "Game")
    void CreateSession(const FString& SessionName);

    UFUNCTION(BlueprintCallable, Category = "Game")
    void JoinSession(const FGuid& SessionId);

    UFUNCTION(BlueprintCallable, Category = "Game")
    void LeaveSession();

    UFUNCTION(BlueprintCallable, Category = "Game")
    void SendChatMessage(const FString& Message);

    UFUNCTION(BlueprintCallable, Category = "Game")
    void PerformAction(const FString& ActionType, const FString& ActionData);

    // Events
    DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnSessionJoined, FGuid, SessionId);
    UPROPERTY(BlueprintAssignable)
    FOnSessionJoined OnSessionJoined;

    DECLARE_DYNAMIC_MULTICAST_DELEGATE(FOnSessionLeft);
    UPROPERTY(BlueprintAssignable)
    FOnSessionLeft OnSessionLeft;

protected:
    UPROPERTY()
    UBannouConnection* Connection;

    FGuid CurrentSessionId;
};

// GameSessionManager.cpp

#include "GameSessionManager.h"

void UGameSessionManager::Initialize(UBannouConnection* InConnection)
{
    Connection = InConnection;
}

void UGameSessionManager::CreateSession(const FString& SessionName)
{
    FCreateSessionRequest Request;
    Request.Name = SessionName;
    Request.MaxPlayers = 8;
    Request.IsPublic = true;

    FString JsonPayload;
    FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

    FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/sessions/create"));

    Connection->SendRequest(ServiceGuid, JsonPayload,
        [this](bool bSuccess, const FString& Response)
        {
            if (bSuccess)
            {
                FCreateSessionResponse SessionResponse;
                FJsonObjectConverter::JsonObjectStringToUStruct(Response, &SessionResponse);

                CurrentSessionId = SessionResponse.SessionId;
                OnSessionJoined.Broadcast(CurrentSessionId);

                UE_LOG(LogTemp, Log, TEXT("Created session: %s"), *CurrentSessionId.ToString());
            }
            else
            {
                UE_LOG(LogTemp, Error, TEXT("Failed to create session: %s"), *Response);
            }
        });
}

void UGameSessionManager::JoinSession(const FGuid& SessionId)
{
    FJoinSessionRequest Request;
    Request.SessionId = SessionId;

    FString JsonPayload;
    FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

    FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/sessions/join-session"));

    Connection->SendRequest(ServiceGuid, JsonPayload,
        [this, SessionId](bool bSuccess, const FString& Response)
        {
            if (bSuccess)
            {
                CurrentSessionId = SessionId;
                OnSessionJoined.Broadcast(CurrentSessionId);
                UE_LOG(LogTemp, Log, TEXT("Joined session: %s"), *SessionId.ToString());
            }
            else
            {
                UE_LOG(LogTemp, Error, TEXT("Failed to join session: %s"), *Response);
            }
        });
}

void UGameSessionManager::LeaveSession()
{
    if (!CurrentSessionId.IsValid())
    {
        return;
    }

    FLeaveSessionRequest Request;
    Request.SessionId = CurrentSessionId;

    FString JsonPayload;
    FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

    FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/sessions/leave-session"));

    Connection->SendRequest(ServiceGuid, JsonPayload,
        [this](bool bSuccess, const FString& Response)
        {
            CurrentSessionId.Invalidate();
            OnSessionLeft.Broadcast();
            UE_LOG(LogTemp, Log, TEXT("Left session"));
        });
}

void UGameSessionManager::SendChatMessage(const FString& Message)
{
    if (!CurrentSessionId.IsValid())
    {
        return;
    }

    FChatRequest Request;
    Request.SessionId = CurrentSessionId;
    Request.Message = Message;

    FString JsonPayload;
    FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

    FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/sessions/chat"));

    Connection->SendRequest(ServiceGuid, JsonPayload,
        [](bool bSuccess, const FString& Response)
        {
            // Chat is fire-and-forget, no response handling needed
        });
}

void UGameSessionManager::PerformAction(const FString& ActionType, const FString& ActionData)
{
    if (!CurrentSessionId.IsValid())
    {
        return;
    }

    FGameActionRequest Request;
    Request.SessionId = CurrentSessionId;
    Request.ActionType = ActionType;
    Request.ActionData = ActionData;

    FString JsonPayload;
    FJsonObjectConverter::UStructToJsonObjectString(Request, JsonPayload);

    FGuid ServiceGuid = Connection->GetServiceGuid(TEXT("POST:/sessions/actions"));

    Connection->SendRequest(ServiceGuid, JsonPayload,
        [](bool bSuccess, const FString& Response)
        {
            if (!bSuccess)
            {
                UE_LOG(LogTemp, Warning, TEXT("Game action failed: %s"), *Response);
            }
        });
}
```

## Blueprint Integration

All generated types support Blueprint access via UPROPERTY and UFUNCTION macros. Here's an example of a Blueprint-friendly wrapper:

```cpp
// BannouBlueprintLibrary.h

#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "BannouConnection.h"
#include "BannouBlueprintLibrary.generated.h"

UCLASS()
class UBannouBlueprintLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "Bannou", meta = (WorldContext = "WorldContextObject"))
    static UBannouConnection* CreateConnection(UObject* WorldContextObject);

    UFUNCTION(BlueprintPure, Category = "Bannou")
    static FString GetResponseCodeName(uint8 Code);

    UFUNCTION(BlueprintPure, Category = "Bannou")
    static int32 GetResponseHttpStatus(uint8 Code);

    UFUNCTION(BlueprintPure, Category = "Bannou")
    static bool IsResponseSuccess(uint8 Code);
};
```

```cpp
// BannouBlueprintLibrary.cpp

#include "BannouBlueprintLibrary.h"

UBannouConnection* UBannouBlueprintLibrary::CreateConnection(UObject* WorldContextObject)
{
    return NewObject<UBannouConnection>(WorldContextObject);
}

FString UBannouBlueprintLibrary::GetResponseCodeName(uint8 Code)
{
    return Bannou::GetResponseCodeName(static_cast<Bannou::EResponseCode>(Code));
}

int32 UBannouBlueprintLibrary::GetResponseHttpStatus(uint8 Code)
{
    return Bannou::MapToHttpStatus(static_cast<Bannou::EResponseCode>(Code));
}

bool UBannouBlueprintLibrary::IsResponseSuccess(uint8 Code)
{
    return Bannou::IsSuccess(static_cast<Bannou::EResponseCode>(Code));
}
```

These examples demonstrate the core integration patterns. Adapt them to your specific game architecture and requirements.
