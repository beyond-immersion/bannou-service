# Bannou Architecture & Design Philosophy

This document explains the architectural decisions and design philosophy behind Bannou, a schema-driven monoservice platform for multiplayer games.

## Overview

Bannou is a **monoservice** - a single codebase that can deploy as anything from a monolith (all services on one machine) to a fully distributed microservices architecture (services spread across thousands of nodes). This flexibility comes from three key architectural decisions:

1. **Schema-First Development** - OpenAPI specifications are the single source of truth
2. **Plugin Architecture** - Each service is an independent, loadable assembly
3. **Infrastructure Libs** - Infrastructure concerns (state, messaging, service invocation) are abstracted via lib-state, lib-messaging, and lib-mesh

Together, these enable Bannou to scale from local development to supporting 100,000+ concurrent AI-driven NPCs without code changes.

## Why Monoservice?

Traditional architectures force a choice:
- **Monoliths**: Simple to develop and deploy, but hard to scale individual components
- **Microservices**: Independently scalable, but complex to develop and coordinate

Bannou's monoservice pattern provides both benefits:

| Characteristic | Monolith | Microservices | Bannou Monoservice |
|---------------|----------|---------------|-------------------|
| Local development | Simple | Complex | Simple |
| Code sharing | Easy | Hard | Easy |
| Independent scaling | No | Yes | Yes |
| Deployment complexity | Low | High | Configurable |
| Service coordination | N/A | Distributed | Unified |

**How it works**: The same codebase compiles into one binary. Environment variables determine which services are active:

```bash
# Development: Everything runs locally
SERVICES_ENABLED=true

# Production: Only specific services on this node
SERVICES_ENABLED=false
ACCOUNT_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
```

## Schema-First Development

Every Bannou service starts with an OpenAPI specification. This schema is the **single source of truth** - it defines:

- API endpoints and their request/response models
- Validation rules and constraints
- Documentation and examples
- Permission requirements

Code generation then creates:
- **Controllers** - HTTP routing and request handling
- **Service interfaces** - Method signatures for business logic
- **Request/Response models** - Typed data structures with validation
- **Client libraries** - TypeScript clients for game integration
- **Configuration classes** - Typed service configuration

**The workflow**:
```
Schema (YAML) → Code Generation → Implementation → Testing
                     ↓
              Controllers, Models, Clients, Tests
```

This approach solves several problems:
- **No drift**: Code always matches the schema because it's generated from it
- **Consistent patterns**: All services follow identical structures
- **Rapid development**: New services go from concept to production in under a day
- **Type safety**: Compile-time validation across service boundaries

## POST-Only API Pattern

All Bannou service APIs use POST requests exclusively. This isn't RESTful tradition - it's a deliberate design choice that enables **zero-copy message routing**.

**The problem with path parameters**:
```
GET /account/{accountId}     # accountId varies - can't map to single GUID
GET /account/abc123          # Different path than /account/xyz789
```

**The solution**:
```
POST /account/get            # Static path - maps to exactly one GUID
Body: {"account_id": "abc123"}
```

When a WebSocket message arrives, the Connect service extracts a 16-byte GUID from the binary header and routes the message without ever examining the payload. This works only when each endpoint has exactly one GUID - which requires static paths.

**Exception**: Website service uses traditional REST patterns for browser compatibility (bookmarkable URLs, SEO, caching).

## Plugin Architecture

Each service is an independent **plugin** - a .NET assembly that can be loaded or excluded at startup:

```
plugins/lib-account/              # Account service plugin
├── Generated/                    # Auto-generated from schema (never edit)
│   ├── AccountController.cs      # HTTP routing
│   ├── IAccountService.cs        # Service interface
│   └── AccountServiceConfiguration.cs  # Typed config class
├── AccountService.cs             # Business logic implementation
├── AccountServiceModels.cs       # Internal data models (storage, cache, DTOs)
├── AccountServiceEvents.cs       # Event handlers (partial class)
├── AccountServicePlugin.cs       # Plugin registration
└── lib-account.csproj

bannou-service/Generated/         # Shared generated code (never edit)
├── Models/AccountModels.cs       # Request/response models
├── Clients/AccountClient.cs      # Client for inter-service calls
└── Events/AccountEventsModels.cs # Event models
```

Use `make print-models PLUGIN="account"` to inspect request/response model shapes instead of reading generated files directly.

**Key principles**:
- **One schema, one plugin** - Clear ownership and boundaries
- **Generated code is untouchable** - Never edit files in `Generated/`
- **Clean separation** - Business logic in `*Service.cs`, internal models in `*ServiceModels.cs`, event handlers in `*ServiceEvents.cs`
- **Assembly loading** - Plugins loaded based on environment configuration

**Service registration**:
```csharp
[BannouService("account", typeof(IAccountService), lifetime: ServiceLifetime.Scoped)]
public class AccountService : IAccountService
{
    // Business logic implementation
}
```

The `[BannouService]` attribute enables automatic discovery and dependency injection.

## Infrastructure Libs

Bannou uses three infrastructure libraries to abstract infrastructure concerns, making services portable across environments:

### State Management (lib-state)
Services don't know if they're using Redis, MySQL, or any other store:
```csharp
// In constructor - use StateStoreDefinitions constants (schema-first)
_stateStore = stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);

// Usage
await _stateStore.SaveAsync(key, data);
var data = await _stateStore.GetAsync(key);
```

State stores are defined in `schemas/state-stores.yaml` and code is generated to `StateStoreDefinitions.cs` - change the schema, not the code.

The factory provides specialized interfaces for backend-specific capabilities: `ICacheableStateStore<T>` for Redis sets/sorted sets, `IQueryableStateStore<T>` for MySQL LINQ queries, and `IRedisOperations` for Lua scripts and atomic operations. See [Plugin Development Guide](guides/PLUGIN_DEVELOPMENT.md) for details.

### Pub/Sub Messaging (lib-messaging)
Services publish and subscribe to events without knowing the message broker details:
```csharp
// Publisher
await _messageBus.PublishAsync("account.created", event);

// Subscriber
await _messageSubscriber.SubscribeAsync<AccountCreatedEvent>(
    "account.created",
    async (evt, ct) => await HandleAccountCreatedAsync(evt, ct));
```

RabbitMQ is used underneath, but services only interact with the IMessageBus abstraction.

### Service Invocation (lib-mesh)
Services call each other through lib-mesh without hardcoded addresses:
```csharp
var response = await _meshClient.InvokeMethodAsync<Request, Response>(
    "auth",        // Target service (resolved by mesh)
    "validate",    // Method
    request
);
```

The mesh handles service discovery, load balancing, and endpoint caching via YARP.

### The "Bannou" Omnipotent Routing

In development, all services route to a single `bannou` app-id:
```csharp
public string GetAppIdForService(string serviceName)
{
    // Development: everything routes to "bannou"
    return _serviceMappings.GetValueOrDefault(serviceName, "bannou");
}
```

In production, the mesh can dynamically route to distributed services based on Redis-stored routing tables updated by the Orchestrator.

## WebSocket-First Architecture

Bannou uses a **Connect service edge gateway** that routes all client communication:

```
Client ──WebSocket──► Connect Service ──mesh──► Backend Services
                           │
                     Binary Routing Header
                     (31 bytes, zero-copy)
```

### Why WebSocket-First?

Traditional HTTP per-request has overhead:
- Connection establishment for each request
- Header parsing and serialization
- No server-push capability

WebSocket provides:
- Persistent connections with minimal overhead
- Bidirectional communication (server can push to clients)
- Binary protocol efficiency

### Hybrid Protocol Design

Messages use binary headers for routing, JSON payloads for data:

```
┌─────────────────────────────────────────────────────────┐
│ Binary Header (31 bytes)                                │
├──────────┬─────────┬──────────┬──────────────┬──────────┤
│ Flags    │ Channel │ Sequence │ Service GUID │ Msg ID   │
│ (1 byte) │ (2)     │ (4)      │ (16)         │ (8)      │
├─────────────────────────────────────────────────────────┤
│ JSON Payload (variable length)                          │
│ { "account_id": "abc123", ... }                         │
└─────────────────────────────────────────────────────────┘
```

**Zero-copy routing**: Connect extracts the GUID, routes to the target service, and forwards the JSON payload unchanged - no deserialization required.

**Client-salted GUIDs**: Each client gets unique GUIDs for the same endpoints, preventing cross-client security exploits:
```
Client A: /account/get → GUID abc123...
Client B: /account/get → GUID xyz789...  (different!)
```

### Capability Manifest

Clients receive a dynamic list of available APIs when connecting:
```json
{
  "capabilities": [
    {
      "name": "account/get",
      "guid": "abc123...",
      "method": "POST",
      "requires_auth": true
    }
  ]
}
```

This manifest updates in real-time as:
- User authenticates (more APIs become available)
- Permissions change (admin access granted/revoked)
- Services deploy updates (new endpoints appear)

## Deployment Flexibility

The same binary supports multiple deployment patterns:

### Local Development
All services, single process:
```bash
SERVICES_ENABLED=true
docker-compose up
```

### Testing Configurations
Minimal services for specific test scenarios:
```bash
SERVICES_ENABLED=false
TESTING_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
```

### Production Distribution
Services distributed by function:
```bash
# Auth nodes
AUTH_SERVICE_ENABLED=true
ACCOUNT_SERVICE_ENABLED=true

# NPC processing nodes
BEHAVIOR_SERVICE_ENABLED=true
CHARACTER_SERVICE_ENABLED=true

# Game state nodes
GAME_SESSION_SERVICE_ENABLED=true
WORLD_SERVICE_ENABLED=true
```

### Orchestrator-Managed
The Orchestrator service can dynamically manage deployments using presets:
```yaml
# provisioning/orchestrator/presets/http-tests.yaml
services:
  - auth
  - account
  - testing
```

## Service Communication Patterns

### Synchronous (Request/Response)
For operations requiring immediate results:
```csharp
var (status, response) = await _authClient.ValidateTokenAsync(request);
```

### Asynchronous (Events)
For operations that don't require immediate response:
```csharp
await _messageBus.PublishAsync("account.deleted", event);
```

### Event-Driven Flows
Complex operations chain through events:
```
Account Deleted
    → Auth Service: Invalidate all sessions
        → Connect Service: Close WebSocket connections
            → Client: Receives disconnect with reconnection token
```

## Testing Architecture

Bannou implements three-tier testing:

| Tier | Purpose | Command |
|------|---------|---------|
| Unit | Service logic in isolation | `make test` |
| HTTP | Service-to-service via HTTP | `make test-http` |
| Edge | Full WebSocket protocol | `make test-edge` |

**Schema-driven test generation** creates tests automatically from OpenAPI specs:
- Success scenarios for each endpoint
- Validation error scenarios
- Authorization scenarios

**Dual-transport validation** ensures HTTP and WebSocket paths produce identical results.

## Further Reading

- [WebSocket Protocol Specification](WEBSOCKET-PROTOCOL.md) - Binary protocol details
- [Plugin Development Guide](guides/PLUGIN-DEVELOPMENT.md) - Creating and extending services
- [Deployment Guide](operations/DEPLOYMENT.md) - Production deployment patterns
- [Testing Guide](operations/TESTING.md) - Testing architecture and commands
- [Development Rules](reference/TENETS.md) - Mandatory development constraints
