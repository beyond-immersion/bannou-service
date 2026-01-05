# Bannou Server SDK

Server SDK for the Bannou service platform. Use this for **game servers** and **internal services** that need:
- Mesh service-to-service calls via generated ServiceClients
- WebSocket connections for event reception
- Full access to all Bannou APIs

## When to Use This SDK

- **Game Servers** (e.g., Stride3D game server) that need to call Bannou services directly
- **Internal Microservices** that communicate via mesh service invocation
- **External Servers** that connect via WebSocket for event reception

## Features

- ✅ Type-safe service clients (`AccountsClient`, `AuthClient`, etc.)
- ✅ Request/Response models for all APIs
- ✅ Event models for pub/sub messaging
- ✅ WebSocket binary protocol (31-byte header)
- ✅ `BannouClient` for WebSocket connections
- ✅ Mesh service routing with dynamic app-id resolution

## Usage

### Using Service Clients

```csharp
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;

// Service clients use mesh routing
var accountClient = new AccountClient();
var authClient = new AuthClient();

var response = await accountClient.CreateAccountAsync(new CreateAccountRequest
{
    Username = "user",
    Password = "password"
});
```

### Using WebSocket Connection

```csharp
using BeyondImmersion.Bannou.Client.SDK;

var client = new BannouClient("wss://connect.bannou.example.com/ws");
await client.ConnectAsync();

// Receive events via WebSocket
client.OnEvent += (sender, e) => Console.WriteLine($"Event: {e.EventType}");
```

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.SDK
```

## See Also

- **Bannou.Client.SDK** - For game clients that only use WebSocket (lightweight, no service clients)
