# Bannou Server SDK

Server SDK for game servers and internal services communicating with Bannou.

## Overview

The Server SDK provides everything a game server needs to integrate with Bannou:
- **Generated service clients** for type-safe API calls (`IAuthClient`, `IAccountClient`, etc.)
- **Mesh service routing** for dynamic service-to-service communication
- **Event subscription** for real-time updates via pub/sub
- **WebSocket client** for Connect service integration
- **All models and events** from the Client SDK plus server-specific infrastructure

## Quick Start

```csharp
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Character;

// Set up dependency injection (see full example below)
var services = new ServiceCollection();
services.AddBannouServiceClients();
services.AddScoped<IAuthClient, AuthClient>();
services.AddScoped<IAccountClient, AccountClient>();
services.AddScoped<ICharacterClient, CharacterClient>();

var provider = services.BuildServiceProvider();

// Use service clients
using var scope = provider.CreateScope();
var authClient = scope.ServiceProvider.GetRequiredService<IAuthClient>();

var response = await authClient.ValidateTokenAsync(new ValidateTokenRequest
{
    Token = "eyJhbG..."
});

if (response.IsValid)
{
    Console.WriteLine($"User: {response.AccountId}");
}
```

## Architecture

```
┌─────────────────┐                    ┌─────────────────┐
│   Game Server   │◄── mesh calls ────►│ Bannou Services │
│  (Server SDK)   │                    │ via YARP proxy  │
└────────┬────────┘                    └─────────────────┘
         │
         │ WebSocket (optional)
         ▼
┌─────────────────┐
│ Connect Service │
│   (Events)      │
└─────────────────┘
```

Game servers communicate with Bannou in two ways:
1. **Mesh invocation** - Direct service-to-service calls via generated clients
2. **WebSocket** - Event reception through the Connect service gateway

## Generated Service Clients

The SDK includes NSwag-generated clients for all Bannou services:

| Client | Service | Purpose |
|--------|---------|---------|
| `IAuthClient` | auth | Authentication, JWT validation |
| `IAccountClient` | account | User account management |
| `ICharacterClient` | character | Character CRUD operations |
| `IGameSessionClient` | game-session | Game session lifecycle |
| `ISubscriptionClient` | subscription | Service subscriptions |
| `IPermissionClient` | permission | Permission queries |
| `IRealmClient` | realm | World/realm management |
| `ILocationClient` | location | Player location tracking |
| `ISpeciesClient` | species | Character species definitions |
| `IRelationshipClient` | relationship | Player relationships |
| `IBehaviorClient` | behavior | NPC behavior management |
| `IDocumentationClient` | documentation | Knowledge base access |
| `IMessagingClient` | messaging | Pub/sub infrastructure |
| `IStateClient` | state | State store operations |
| `IMeshClient` | mesh | Service discovery |
| `IOrchestratorClient` | orchestrator | Deployment management |
| `IAssetClient` | asset | Asset storage (MinIO) |

## Dependency Injection Setup

Full DI configuration for a game server:

```csharp
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.ServiceClients;

var services = new ServiceCollection();

// Logging
services.AddLogging(builder => builder.AddConsole());

// Mesh configuration (Redis-based service discovery)
var meshConfig = new MeshServiceConfiguration
{
    RedisConnectionString = "bannou-redis:6379",
    UseLocalRouting = false,
    RedisConnectionTimeoutSeconds = 60
};
services.AddSingleton(meshConfig);

// Mesh infrastructure
services.AddSingleton<IMeshRedisManager, MeshRedisManager>();
services.AddSingleton<IMeshInvocationClient>(sp =>
{
    var redis = sp.GetRequiredService<IMeshRedisManager>();
    var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
    return new MeshInvocationClient(redis, logger);
});

// Bannou service client infrastructure
services.AddBannouServiceClients();

// Register individual clients
services.AddScoped<IAuthClient, AuthClient>();
services.AddScoped<IAccountClient, AccountClient>();
services.AddScoped<ICharacterClient, CharacterClient>();
services.AddScoped<IGameSessionClient, GameSessionClient>();
// ... add more as needed

var provider = services.BuildServiceProvider();

// Initialize mesh connection
var meshManager = provider.GetRequiredService<IMeshRedisManager>();
await meshManager.InitializeAsync();
```

## Mesh Service Routing

Service clients use mesh routing for service discovery:

```csharp
// Mesh automatically resolves service endpoints
// In development: all services route to "bannou" app-id
// In production: Redis routing tables determine endpoints

var characterClient = serviceProvider.GetRequiredService<ICharacterClient>();

// This call:
// 1. Looks up "character" service in mesh routing table
// 2. Resolves to actual HTTP endpoint
// 3. Executes request via YARP proxy
var character = await characterClient.GetCharacterAsync(new GetCharacterRequest
{
    CharacterId = "char-123"
});
```

## Error Handling

Service clients throw `ApiException` for HTTP errors:

```csharp
try
{
    var response = await authClient.ValidateTokenAsync(request);
}
catch (ApiException<ValidationProblem> ex)
{
    // 400 Bad Request with validation details
    Console.WriteLine($"Validation failed: {ex.Result.Errors}");
}
catch (ApiException ex)
{
    // Other HTTP errors
    Console.WriteLine($"API error {ex.StatusCode}: {ex.Message}");
}
```

## Event Subscription

For real-time events, use the WebSocket client:

```csharp
using BeyondImmersion.Bannou.Client.SDK;

var client = new BannouClient();
// For internal nodes, skip JWT and use service token or network trust:
// await client.ConnectInternalAsync("ws://bannou-internal/connect", serviceToken: "shared-secret");
await client.ConnectWithTokenAsync(connectUrl, serverAccessToken);

client.OnEvent += (sender, eventData) =>
{
    switch (eventData.EventName)
    {
        case "game-session.player_joined":
            var joinEvent = JsonSerializer.Deserialize<PlayerJoinedEvent>(eventData.Payload);
            HandlePlayerJoined(joinEvent);
            break;

        case "character.updated":
            var updateEvent = JsonSerializer.Deserialize<CharacterUpdatedEvent>(eventData.Payload);
            HandleCharacterUpdate(updateEvent);
            break;
    }
};
```

## Real-World Example: HTTP Tester

The `http-tester/` project demonstrates comprehensive Server SDK usage:

- **Program.cs** - Complete DI setup with mesh, messaging, and all service clients
- **AuthTestHandler.cs** - Authentication flows via `IAuthClient`
- **CharacterTestHandler.cs** - Character CRUD via `ICharacterClient`
- **GameSessionTestHandler.cs** - Game session lifecycle via `IGameSessionClient`
- **PermissionTestHandler.cs** - Permission queries via `IPermissionClient`
- **MeshTestHandler.cs** - Service discovery via `IMeshClient`

Key patterns from http-tester:

```csharp
// Service readiness check via Permission service
var permissionClient = provider.GetRequiredService<IPermissionClient>();
var services = await permissionClient.GetRegisteredServicesAsync(new ListServicesRequest());
Console.WriteLine($"Registered services: {string.Join(", ", services.Services.Select(s => s.ServiceId))}");

// Authentication flow
var authClient = provider.GetRequiredService<IAuthClient>();
var loginResponse = await authClient.LoginAsync(new LoginRequest
{
    Email = "user@example.com",
    Password = "password123"
});

// Use auth token for subsequent requests
var characterClient = provider.GetRequiredService<ICharacterClient>();
var characters = await characterClient.ListCharactersAsync(new ListCharactersRequest
{
    AccountId = loginResponse.AccountId
});
```

## Health Checking

Wait for Bannou services before starting your game server:

```csharp
async Task<bool> WaitForBannouAsync(IPermissionClient permissionClient, TimeSpan timeout)
{
    var expectedServices = new[] { "auth", "account", "character", "game-session" };
    var stopwatch = Stopwatch.StartNew();

    while (stopwatch.Elapsed < timeout)
    {
        try
        {
            var response = await permissionClient.GetRegisteredServicesAsync(new ListServicesRequest());
            var registered = response.Services.Select(s => s.ServiceId).ToHashSet();

            if (expectedServices.All(s => registered.Contains(s)))
            {
                Console.WriteLine("All required Bannou services ready");
                return true;
            }
        }
        catch { }

        await Task.Delay(2000);
    }

    return false;
}
```

## Package Contents

| Component | Description |
|-----------|-------------|
| `I*Client` | Generated service clients for all Bannou services |
| `*Client` | Client implementations using mesh routing |
| `IServiceClient` | Base interface for service clients |
| `IMeshInvocationClient` | Mesh invocation infrastructure |
| `BannouClient` | WebSocket client (from Client SDK) |
| `*Models.cs` | All request/response models |
| `*Events.cs` | All event types |
| `Behavior/Runtime/*` | ABML behavior interpreter |

## When NOT to Use This SDK

Use **Bannou.Client.SDK** instead if you:
- Only need WebSocket communication (game clients)
- Want minimal dependencies
- Don't need service-to-service calls

## Dependencies

- `System.Net.WebSockets.Client` - WebSocket connectivity
- `Microsoft.Extensions.Logging.Abstractions` - Logging infrastructure

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.SDK
```

## Configuration

Environment variables for mesh configuration:

| Variable | Description | Default |
|----------|-------------|---------|
| `MESH_REDIS_HOST` | Redis host for service discovery | `bannou-redis` |
| `MESH_REDIS_PORT` | Redis port | `6379` |
| `MESH_REDIS_CONNECTION_STRING` | Full connection string (overrides host/port) | - |

## License

MIT License
