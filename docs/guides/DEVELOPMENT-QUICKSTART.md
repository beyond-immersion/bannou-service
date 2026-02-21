# Bannou Development Quickstart

> **5-minute setup for experienced developers**

## Choose Your Path

| I want to... | Time | Guide |
|--------------|------|-------|
| **Run Bannou locally** | 5 min | [Local Development](#local-development) (this page) |
| **Connect a game client** | 15 min | [Client Integration](#client-integration) |
| **Make service-to-service calls** | 10 min | [Service SDK](#service-sdk) |
| **Understand the architecture** | 30 min | [Getting Started](GETTING_STARTED.md) |
| **Add a new service** | 1 hr | [Plugin Development](PLUGIN_DEVELOPMENT.md) |

---

## Local Development

### Prerequisites

- **Ubuntu 22.04+** (or WSL2 on Windows)
- **Docker** with Compose v2
- **Git**

### Install & Run

```bash
# 1. Clone
git clone https://github.com/beyond-immersion/bannou-service.git
cd bannou-service

# 2. Install dev tools (.NET 10, NSwag, Node.js, Python)
./scripts/install-dev-tools.sh
source ~/.bashrc

# 3. Build and verify
make quick

# 4. Start the stack
make up-compose

# 5. Verify it's running
curl http://localhost:8080/health
```

**That's it.** Bannou is running with all 41 services, Redis, RabbitMQ, and MySQL.

### What `install-dev-tools.sh` installs

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10 (preview) | Build and run services |
| .NET Runtime | 9 | NSwag compatibility |
| NSwag | 14.5.0 | Code generation from OpenAPI |
| Python 3 | + ruamel.yaml | Schema processing scripts |
| Node.js | 20+ | EditorConfig tooling (eclint) |

### Essential Commands

```bash
make build              # Build all projects
make generate           # Regenerate from schemas
make format             # Fix formatting
make test               # Run unit tests
make up-compose         # Start Docker stack
make down-compose       # Stop Docker stack
```

---

## Client Integration

Game clients connect via WebSocket using the **BannouClient SDK**.

### Quick Example (C#)

```csharp
using BeyondImmersion.Bannou.Client;

// Connect with credentials
var client = new BannouClient();
await client.ConnectAsync("http://localhost:8080", "user@example.com", "password");

// Or with existing token
await client.ConnectWithTokenAsync("ws://localhost:8080/connect", accessToken, refreshToken);

// Make API calls
var response = await client.InvokeAsync<GetCharacterRequest, CharacterResponse>(
    "POST", "/character/get",
    new GetCharacterRequest { CharacterId = "abc123" });

if (response.IsSuccess)
    Console.WriteLine($"Character: {response.Result.Name}");

// Subscribe to events
client.OnEvent("game-session.player-joined", json => {
    Console.WriteLine($"Player joined: {json}");
});
```

### Connection Flow

1. Client → `POST /auth/login` → JWT tokens
2. Client → WebSocket `/connect` with Bearer token
3. Server → Capability manifest (client-salted GUIDs)
4. Client → Binary messages with GUID headers
5. Server → JSON responses or pushed events

### Key Concepts

- **Capability Manifest**: Dynamic list of available APIs with unique GUIDs
- **Binary Protocol**: 31-byte header + JSON payload for zero-copy routing
- **Client-Salted GUIDs**: Each client gets unique GUIDs (security)
- **Events**: Server pushes events without client request

See [Client Integration Guide](CLIENT_INTEGRATION.md) for full details.

---

## Service SDK

Backend services call each other using **generated service clients**.

### Quick Example (C#)

```csharp
// Inject generated clients via DI
public class MyService
{
    private readonly ICharacterClient _characterClient;
    private readonly IRealmClient _realmClient;

    public MyService(ICharacterClient characterClient, IRealmClient realmClient)
    {
        _characterClient = characterClient;
        _realmClient = realmClient;
    }

    public async Task<CharacterWithRealm> GetCharacterWithRealmAsync(string characterId)
    {
        // Call Character service
        var character = await _characterClient.GetCharacterAsync(
            new GetCharacterRequest { CharacterId = characterId });

        // Call Realm service
        var realm = await _realmClient.GetRealmAsync(
            new GetRealmRequest { RealmId = character.RealmId });

        return new CharacterWithRealm { Character = character, Realm = realm };
    }
}
```

### How It Works

1. **Generated Clients**: NSwag generates typed clients from OpenAPI schemas
2. **Mesh Routing**: `IMeshInvocationClient` handles service discovery via Redis
3. **App-ID Resolution**: Services route to `"bannou"` locally, or distributed app-ids in production
4. **Load Balancing**: Automatic round-robin across healthy endpoints

### Key Patterns

```csharp
// With authorization header
var response = await _accountClient
    .WithAuthorization(jwtToken)
    .GetAccountAsync(request);

// Error handling
try {
    var result = await _someClient.SomeMethodAsync(request);
} catch (MeshInvocationException ex) {
    // ex.AppId, ex.MethodName, ex.StatusCode
}
```

---

## Configuration

Copy `.env.example` to `.env` and configure:

```bash
# Minimal local development (defaults work)
cp .env.example .env

# Critical settings for production
BANNOU_JWT_SECRET=your-64-char-minimum-secret
BANNOU_SERVICE_DOMAIN=your-domain.example.com
# Infrastructure defaults: bannou-redis:6379, bannou-mysql, rabbitmq:5672
```

### Service Enable/Disable

```bash
# All services (default for development)
SERVICES_ENABLED=true

# Selective loading (production)
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
```

---

## Next Steps

| Topic | Guide |
|-------|-------|
| Full setup walkthrough | [Getting Started](GETTING_STARTED.md) |
| Architecture deep-dive | [Bannou Design](../BANNOU_DESIGN.md) |
| WebSocket protocol | [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) |
| Add a service | [Plugin Development](PLUGIN_DEVELOPMENT.md) |
| Run tests | [Testing Guide](TESTING.md) |
| Deploy to production | [Deployment Guide](DEPLOYMENT.md) |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `dotnet: command not found` | Run `source ~/.bashrc` after install |
| `nswag: command not found` | Run `source ~/.bashrc` or reinstall NSwag |
| Port 8080 in use | Stop conflicting service or change `HTTP_WEB_HOST_PORT` |
| Redis connection failed | Ensure `make up-compose` completed successfully |
| Build errors after schema change | Run `make generate && make format` |

For more help, see [Getting Started Troubleshooting](GETTING_STARTED.md#troubleshooting).
