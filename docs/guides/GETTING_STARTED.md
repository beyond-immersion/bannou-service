# Getting Started with Bannou

This guide walks you through setting up Bannou from scratch, understanding its architecture, and integrating with game clients and backend services.

## Table of Contents

1. [Overview](#overview)
2. [System Requirements](#system-requirements)
3. [Development Environment Setup](#development-environment-setup)
4. [Understanding the Architecture](#understanding-the-architecture)
5. [Configuration](#configuration)
6. [Running Bannou](#running-bannou)
7. [Client SDK Integration](#client-sdk-integration)
8. [Service SDK Integration](#service-sdk-integration)
9. [Development Workflow](#development-workflow)
10. [Troubleshooting](#troubleshooting)

---

## Overview

Bannou is a **schema-driven monoservice platform** designed for multiplayer games. Key characteristics:

- **Monoservice**: Single codebase deploys as monolith or distributed microservices
- **Schema-First**: OpenAPI specifications generate 65-80% of service code
- **WebSocket-First**: Binary protocol with zero-copy message routing
- **Plugin Architecture**: 41 independent service plugins, loadable via environment config
- **Infrastructure Abstraction**: Portable across Redis, MySQL, RabbitMQ via lib-state, lib-messaging, lib-mesh

### Why Bannou?

| Traditional Approach | Bannou Approach |
|---------------------|-----------------|
| Choose monolith OR microservices | Same binary scales as either |
| Write controllers, models, clients manually | Auto-generate from OpenAPI |
| Hardcode database connections | Abstract via infrastructure libs |
| REST per-request overhead | Persistent WebSocket with binary routing |

---

## System Requirements

### Minimum Requirements

| Component | Requirement |
|-----------|-------------|
| OS | Ubuntu 22.04+, Debian 12+, or WSL2 on Windows |
| RAM | 8 GB (16 GB recommended) |
| Disk | 10 GB free space |
| Docker | 24.0+ with Compose v2 |
| Git | 2.30+ |

### Installed by `install-dev-tools.sh`

| Tool | Version | Purpose |
|------|---------|---------|
| **.NET SDK** | 10 (preview) | Build and run Bannou services |
| **.NET Runtime** | 9 | Required by NSwag for code generation |
| **ASP.NET Core Runtime** | 9 | Required by NSwag CLI |
| **NSwag** | 14.5.0 | Generate C# controllers, models, clients from OpenAPI |
| **Python 3** | 3.10+ | Schema processing scripts |
| **ruamel.yaml** | Latest | YAML manipulation for code generation |
| **Node.js** | 20+ | EditorConfig tooling |
| **eclint** | Latest | Enforce code formatting (LF line endings) |

---

## Development Environment Setup

### Step 1: Clone the Repository

```bash
git clone https://github.com/beyond-immersion/bannou-service.git
cd bannou-service
```

### Step 2: Install Development Tools

```bash
./scripts/install-dev-tools.sh
```

This script:
1. Detects your package manager (apt, yum, brew)
2. Installs missing system packages
3. Downloads .NET SDK 10 and .NET 9 runtimes
4. Installs NSwag 14.5.0 globally
5. Sets up Python with ruamel.yaml
6. Installs Node.js 20 and eclint
7. Configures your shell PATH

**After installation:**
```bash
source ~/.bashrc  # or ~/.zshrc
```

### Step 3: Verify Installation

```bash
# Check .NET
dotnet --version          # Should show 10.x.x

# Check NSwag
nswag version             # Should show 14.5.0

# Check Python
python3 -c "import ruamel.yaml; print('OK')"

# Check Node/eclint
node --version            # Should show v20+
eclint --version
```

### Step 4: Build and Test

```bash
# Quick development cycle (no Docker)
make quick

# This runs:
# - make clean          (remove generated files)
# - make generate       (regenerate from schemas)
# - make format         (fix formatting)
# - make build          (compile all projects)
# - make test-unit      (run unit tests)
```

Expected output: All steps complete with green checkmarks, 3,300+ tests pass.

---

## Understanding the Architecture

### The Monoservice Pattern

Bannou compiles to a single binary that can run all 41 services or a selective subset:

```
┌─────────────────────────────────────────────────────────┐
│                    bannou (single binary)               │
├─────────────────────────────────────────────────────────┤
│ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │
│ │  Auth   │ │ Account │ │ Connect │ │ Behavior│  ...   │
│ │ Plugin  │ │ Plugin  │ │ Plugin  │ │ Plugin  │        │
│ └─────────┘ └─────────┘ └─────────┘ └─────────┘        │
├─────────────────────────────────────────────────────────┤
│              Infrastructure Abstraction                  │
│  ┌───────────┐  ┌─────────────┐  ┌───────────┐         │
│  │ lib-state │  │lib-messaging│  │ lib-mesh  │         │
│  │  (Redis/  │  │ (RabbitMQ)  │  │  (HTTP    │         │
│  │   MySQL)  │  │             │  │  routing) │         │
│  └───────────┘  └─────────────┘  └───────────┘         │
└─────────────────────────────────────────────────────────┘
```

### Service Selection via Environment

```bash
# Development: All services
SERVICES_ENABLED=true

# Production: Selective
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
```

### Schema-First Development

Every service starts with an OpenAPI YAML schema:

```
schemas/auth-api.yaml
        ↓ (generate-all-services.sh)
plugins/lib-auth/
├── Generated/
│   ├── AuthController.cs      ← HTTP routing (never edit)
│   ├── IAuthService.cs        ← Interface (never edit)
│   ├── AuthModels.cs          ← DTOs (never edit)
│   └── AuthClient.cs          ← Service client (never edit)
└── AuthService.cs             ← YOUR CODE (only manual file)
```

**The workflow:**
1. Edit `schemas/auth-api.yaml`
2. Run `make generate`
3. Implement business logic in `AuthService.cs`
4. Run `make format && make build`

### WebSocket Protocol

Clients connect via WebSocket with a binary header for zero-copy routing:

```
┌─────────────────────────────────────────────────────────┐
│ Binary Header (31 bytes)                                │
├──────────┬─────────┬──────────┬──────────────┬──────────┤
│ Flags    │ Channel │ Sequence │ Service GUID │ Msg ID   │
│ (1 byte) │ (2)     │ (4)      │ (16)         │ (8)      │
├─────────────────────────────────────────────────────────┤
│ JSON Payload (variable length)                          │
└─────────────────────────────────────────────────────────┘
```

The Connect service extracts the GUID and routes to the target service without parsing the JSON payload.

---

## Configuration

### Create Your Configuration

```bash
cp .env.example .env
```

The `.env.example` file is **auto-generated** from schema configuration files. Do not edit it directly; regenerate with:
```bash
python3 scripts/generate-env-example.py
```

### Environment Variable Naming

Pattern: `{SERVICE}_{PROPERTY}` in UPPER_SNAKE_CASE

| Variable | Maps To |
|----------|---------|
| `AUTH_JWT_SECRET` | `AuthServiceConfiguration.JwtSecret` |
| `CONNECT_HEARTBEAT_INTERVAL_SECONDS` | `ConnectServiceConfiguration.HeartbeatIntervalSeconds` |
| `STATE_REDIS_CONNECTION_STRING` | `StateServiceConfiguration.RedisConnectionString` |

### Critical Configuration Sections

#### Authentication (AUTH_*)
```bash
AUTH_JWT_SECRET=your-jwt-secret-key-change-in-production-min-32-chars
AUTH_JWT_ISSUER=bannou-auth
AUTH_JWT_AUDIENCE=bannou-api
AUTH_JWT_EXPIRATION_MINUTES=60
```

#### WebSocket Gateway (CONNECT_*)
```bash
CONNECT_MAX_CONCURRENT_CONNECTIONS=10000
CONNECT_HEARTBEAT_INTERVAL_SECONDS=30
CONNECT_DEFAULT_SERVICES=auth,website
CONNECT_AUTHENTICATED_SERVICES=account,behavior,permission,gamesession
CONNECT_SERVER_SALT=your-server-salt-for-guid-generation
```

#### State Management (STATE_*)
```bash
# Defaults (only override if using non-standard hostnames):
# STATE_REDIS_CONNECTION_STRING=bannou-redis:6379
# STATE_MYSQL_CONNECTION_STRING=server=bannou-mysql;database=bannou;user=guest;password=guest
```

#### Messaging (MESSAGING_*)
```bash
# Defaults (only override if using non-standard hostnames):
# MESSAGING_RABBITMQ_HOST=rabbitmq
# MESSAGING_RABBITMQ_PORT=5672
```

#### Service Mesh (MESH_*)
```bash
MESH_ENDPOINT_HOST=bannou  # Docker service name
```

### Service Enable/Disable Flags

```bash
# Master switch
SERVICES_ENABLED=true

# Individual overrides
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=true
# ... (41 services total)
```

---

## Running Bannou

### Local Development Stack

```bash
# Start all services with infrastructure
make up-compose
```

This starts:
- **bannou**: All 32 services on port 8080
- **rabbitmq**: Message broker on port 5672 (management: 15672)
- **bannou-redis**: Redis on port 6379
- **bannou-mysql**: MySQL on port 3306

Verify:
```bash
curl http://localhost:8080/health
# {"status":"Healthy","version":"..."}

docker ps
# Should show 4 containers running
```

### Stop the Stack

```bash
make down-compose
```

### Additional Stacks

```bash
# With OpenResty edge proxy (external testing)
make up-openresty

# With voice infrastructure (Kamailio + RTPEngine)
make up-compose-voice

# With orchestrator (dynamic service management)
make up-orchestrator
```

### Running Tests

```bash
# Unit tests (no Docker needed)
make test

# Infrastructure tests (Docker health checks)
make test-infrastructure

# HTTP integration tests
make test-http

# WebSocket protocol tests
make test-edge

# Full CI pipeline locally
make test-ci
```

---

## Client SDK Integration

The **BannouClient SDK** enables game clients to communicate with Bannou services via WebSocket.

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client
```

### Connection Patterns

#### Pattern 1: Login with Credentials

```csharp
using BeyondImmersion.Bannou.Client;

var client = new BannouClient();

// Authenticate and connect
await client.ConnectAsync(
    serverUrl: "http://localhost:8080",
    email: "player@example.com",
    password: "secure-password");

Console.WriteLine($"Connected! Session: {client.SessionId}");
```

#### Pattern 2: Connect with Existing Token

```csharp
// If you already have tokens (e.g., from a previous session)
await client.ConnectWithTokenAsync(
    connectUrl: "ws://localhost:8080/connect",
    accessToken: savedAccessToken,
    refreshToken: savedRefreshToken);
```

#### Pattern 3: Register and Connect

```csharp
// For new users
await client.RegisterAndConnectAsync(
    serverUrl: "http://localhost:8080",
    username: "NewPlayer",
    email: "newplayer@example.com",
    password: "secure-password");
```

### Making API Calls

```csharp
// Define your request/response types (or use generated models)
public class GetCharacterRequest
{
    public string CharacterId { get; set; }
}

public class CharacterResponse
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Level { get; set; }
}

// Make the call
var response = await client.InvokeAsync<GetCharacterRequest, CharacterResponse>(
    method: "POST",
    path: "/character/get",
    request: new GetCharacterRequest { CharacterId = "luna-001" },
    timeout: TimeSpan.FromSeconds(5));

if (response.IsSuccess)
{
    Console.WriteLine($"Character: {response.Result.Name}, Level {response.Result.Level}");
}
else
{
    Console.WriteLine($"Error: {response.Error.ErrorName} - {response.Error.Message}");
}
```

### Subscribing to Events

```csharp
// Subscribe to server-pushed events
client.OnEvent("game-session.player-joined", json =>
{
    var evt = JsonSerializer.Deserialize<PlayerJoinedEvent>(json);
    Console.WriteLine($"Player {evt.PlayerId} joined!");
});

client.OnEvent("character.updated", json =>
{
    var evt = JsonSerializer.Deserialize<CharacterUpdatedEvent>(json);
    UpdateCharacterDisplay(evt);
});

// Automatic capability updates
client.OnEvent("connect.capability_manifest", json =>
{
    Console.WriteLine("Available APIs updated!");
    foreach (var api in client.AvailableApis)
    {
        Console.WriteLine($"  {api.Key}");
    }
});
```

### Capability Manifest

After connecting, clients receive a dynamic list of available APIs:

```csharp
// Check what APIs are available
foreach (var api in client.AvailableApis)
{
    Console.WriteLine($"{api.Key}: {api.Value}");
    // Output: "POST:/character/get: 550e8400-e29b-41d4-a716-446655440000"
}

// Check if specific API is available
var guid = client.GetServiceGuid("POST", "/character/get");
if (guid.HasValue)
{
    // API is available for this session
}
```

**Important**: Each client receives **unique GUIDs** for the same endpoints (client-salted). This prevents cross-client security exploits.

### Connection Management

For production use, wrap BannouClient with `BannouConnectionManager` for auto-reconnection:

```csharp
var config = new BannouConnectionConfig
{
    Endpoint = "http://localhost:8080",
    Email = "player@example.com",
    Password = "password",
    AutoReconnect = true,
    HealthCheckIntervalMs = 5000,
    MaxReconnectAttempts = 10
};

var manager = new BannouConnectionManager(config);
await manager.ConnectAsync();

// Use manager.InvokeAsync() instead of client.InvokeAsync()
// Handles reconnection automatically
```

See [Client Integration Guide](CLIENT_INTEGRATION.md) for advanced patterns.

---

## Service SDK Integration

Backend services communicate using **generated service clients** and the mesh infrastructure.

### How It Works

1. **NSwag generates clients** from OpenAPI schemas
2. **Clients are registered** via dependency injection
3. **IMeshInvocationClient** handles routing via Redis service discovery
4. **Round-robin load balancing** across healthy endpoints

### Using Generated Clients

```csharp
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Clients;

[BannouService("my-service", typeof(IMyService))]
public partial class MyService : IMyService
{
    private readonly ICharacterClient _characterClient;
    private readonly IRealmClient _realmClient;
    private readonly IRelationshipClient _relationshipClient;

    public MyService(
        ICharacterClient characterClient,
        IRealmClient realmClient,
        IRelationshipClient relationshipClient,
        ILogger<MyService> logger)
    {
        _characterClient = characterClient;
        _realmClient = realmClient;
        _relationshipClient = relationshipClient;
    }

    public async Task<(StatusCodes, MyResponse?)> GetEnrichedDataAsync(
        MyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Call Character service
        var characterResponse = await _characterClient.GetCharacterAsync(
            new GetCharacterRequest { CharacterId = request.CharacterId },
            cancellationToken);

        // Call Realm service
        var realmResponse = await _realmClient.GetRealmAsync(
            new GetRealmRequest { RealmId = characterResponse.RealmId },
            cancellationToken);

        // Call Relationship service
        var relationships = await _relationshipClient.ListByEntityAsync(
            new ListByEntityRequest { EntityId = request.CharacterId },
            cancellationToken);

        return (StatusCodes.Ok, new MyResponse
        {
            Character = characterResponse,
            Realm = realmResponse,
            Relationships = relationships.Items
        });
    }
}
```

### Authorization Headers

```csharp
// Pass JWT token to downstream service
var response = await _accountClient
    .WithAuthorization(userJwtToken)
    .GetAccountAsync(request, cancellationToken);
```

### Error Handling

```csharp
try
{
    var result = await _someClient.SomeMethodAsync(request, ct);
}
catch (MeshInvocationException ex)
{
    _logger.LogError(
        "Service call failed: AppId={AppId}, Method={Method}, Status={Status}",
        ex.AppId,
        ex.MethodName,
        ex.StatusCode);

    return (StatusCodes.ServiceUnavailable, null);
}
```

### Service Discovery

The mesh automatically handles:
- **Endpoint registration** in Redis when services start
- **Health monitoring** via periodic heartbeats
- **Load balancing** across multiple instances
- **Failover** when endpoints become unhealthy

In development, all services route to `"bannou"` (local instance). In production, the orchestrator manages dynamic routing.

---

## Development Workflow

### Daily Commands

```bash
# After pulling changes
make sync              # Update submodules

# After editing schemas
make generate          # Regenerate code
make format            # Fix formatting
make build             # Compile

# Before committing
make format            # Ensure formatting
make test              # Run unit tests
```

### Schema-First Development Cycle

1. **Edit Schema**: `schemas/my-service-api.yaml`
2. **Generate**: `make generate-services PLUGIN=my-service`
3. **Implement**: Edit `plugins/lib-my-service/MyServiceService.cs`
4. **Build**: `make build`
5. **Test**: `make test PLUGIN=my-service`
6. **Format**: `make format`

### Adding a New Endpoint

1. Add endpoint to `schemas/my-service-api.yaml`:
   ```yaml
   /my-service/new-endpoint:
     post:
       operationId: newEndpoint
       summary: Does something new
       x-permissions: [user]
       requestBody:
         content:
           application/json:
             schema:
               $ref: '#/components/schemas/NewEndpointRequest'
       responses:
         '200':
           description: Success
           content:
             application/json:
               schema:
                 $ref: '#/components/schemas/NewEndpointResponse'
   ```

2. Regenerate:
   ```bash
   make generate-services PLUGIN=my-service
   make format
   ```

3. Implement in `MyServiceService.cs`:
   ```csharp
   public async Task<(StatusCodes, NewEndpointResponse?)> NewEndpointAsync(
       NewEndpointRequest request,
       CancellationToken cancellationToken = default)
   {
       // Your implementation
       return (StatusCodes.Ok, new NewEndpointResponse { ... });
   }
   ```

4. Build and test:
   ```bash
   make build
   make test PLUGIN=my-service
   ```

---

## Troubleshooting

### Installation Issues

| Problem | Solution |
|---------|----------|
| `dotnet: command not found` | Run `source ~/.bashrc` or restart terminal |
| `nswag: command not found` | Run `dotnet tool install --global NSwag.ConsoleCore --version 14.5.0` |
| NSwag version mismatch | Run `dotnet tool update --global NSwag.ConsoleCore --version 14.5.0` |
| Python import error | Run `pip3 install ruamel.yaml` |
| eclint not found | Run `npm install -g eclint` |

### Build Issues

| Problem | Solution |
|---------|----------|
| CS1591 (missing XML docs) | Add `description` to schema property |
| Type mismatch in generated code | Check schema types, regenerate |
| Circular dependency | Check service client usage, use interfaces |
| Line ending errors | Run `make format` |

### Docker Issues

| Problem | Solution |
|---------|----------|
| Port 8080 in use | Change `HTTP_WEB_HOST_PORT` in `.env` |
| Container won't start | Check `docker logs bannou` |
| Redis connection failed | Ensure Redis container is running |
| MySQL connection failed | Wait 30s for MySQL to initialize |

### Runtime Issues

| Problem | Solution |
|---------|----------|
| 401 Unauthorized | Check JWT_SECRET matches across services |
| 503 Service Unavailable | Check target service is enabled and healthy |
| WebSocket disconnect | Check CONNECT_HEARTBEAT_INTERVAL_SECONDS |
| Slow responses | Check Redis connection, enable metrics |

### Getting Help

1. Check [Discord](https://discord.gg/3eAGYwF3rE) for community support
2. Search [GitHub Issues](https://github.com/beyond-immersion/bannou-service/issues)
3. Review [Development Rules](../reference/TENETS.md) for architectural constraints

---

## Next Steps

| Topic | Guide |
|-------|-------|
| Quick reference | [Quickstart](QUICKSTART.md) |
| WebSocket protocol details | [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) |
| Create a new service | [Plugin Development](PLUGIN_DEVELOPMENT.md) |
| Testing patterns | [Testing Guide](TESTING.md) |
| Production deployment | [Deployment Guide](DEPLOYMENT.md) |
| Architecture deep-dive | [Bannou Design](../BANNOU_DESIGN.md) |
| Development rules | [Tenets](../reference/TENETS.md) |
