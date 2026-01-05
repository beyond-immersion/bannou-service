# Bannou Client SDK

Lightweight client SDK for the Bannou service platform. Use this for **game clients** that:
- Connect via WebSocket only
- Don't need service-to-service calls
- Want minimal dependencies

## When to Use This SDK

- **Game Clients** (e.g., Stride3D client, Unity client) that connect via WebSocket
- **Web Clients** that use WebSocket for real-time communication
- Any client that communicates through the Connect service gateway

## What's NOT Included

This SDK **does not include**:
- ❌ ServiceClients (`AccountClient`, `AuthClient`, etc.) - use `Bannou.SDK` if you need these
- ❌ Server-side infrastructure dependencies

## Features

- ✅ Request/Response models for all APIs
- ✅ Event models for pub/sub messaging
- ✅ WebSocket binary protocol (31-byte header)
- ✅ `BannouClient` for WebSocket connections
- ✅ Minimal dependencies (smaller package)

## Usage

```csharp
using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Account;

// Connect via WebSocket
var client = new BannouClient("wss://connect.bannou.example.com/ws");
await client.ConnectAsync();

// Use models for requests/responses
var loginRequest = new LoginRequest
{
    Username = "user",
    Password = "password"
};

// Send via WebSocket binary protocol
var response = await client.SendRequestAsync<LoginRequest, LoginResponse>(
    serviceName: "auth",
    request: loginRequest
);
```

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.SDK
```

## See Also

- **Bannou.SDK** - For game servers that need service clients
