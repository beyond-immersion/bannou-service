# Bannou Service

[![Build Status](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml/badge.svg?branch=master&event=push)](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml)

Bannou Service is a versatile ASP.NET Core application designed to provide a WebSocket-first microservices architecture for massively multiplayer online games. Featuring an intelligent Connect service edge gateway that routes messages via service GUIDs without payload inspection, Bannou enables zero-copy message routing and seamless dual-transport communication (HTTP for development, WebSocket for production). The platform uses schema-driven development with NSwag code generation to ensure API consistency across all services. Primarily designed to support Arcadia, a revolutionary MMORPG with AI-driven NPCs, Bannou becomes the foundation of the universal cloud-based platform for developing and hosting multiplayer video games, tentatively called "CelestialLink".

**⚠️ IMPORTANT**: For all Bannou development tasks, always reference **API-DESIGN.md** (available in Technical Architecture knowledge base section) first. This document defines the authoritative schema-driven development approach, consolidated service architecture (one plugin per service), and implementation patterns that must be followed for all Bannou services.

## Table of Contents

- [Bannou Service](#bannou-service)
  - [Table of Contents](#table-of-contents)
  - [Features](#features)
  - [WebSocket-First Architecture](#websocket-first-architecture)
  - [Schema-Driven Development](#schema-driven-development)
  - [Testing & Development Commands](#testing--development-commands)
  - [Orchestrator Service](#orchestrator-service)
  - [Local Deploy (Compose)](#local-deploy-compose)
    - [Prerequisites](#prerequisites)
    - [Manual](#manual)
    - [Make](#make)
  - [Extending the Service](#extending-the-service)
    - [Adding APIs](#adding-apis)
    - [Implementing IDaprService](#implementing-idaprservice)
    - [Implementing IServiceConfiguration](#implementing-iserviceconfiguration)
    - [Implementing IDaprController](#implementing-idaprcontroller)
    - [Implementing IServiceAttribute](#implementing-iserviceattribute)
  - [Deployment Notes](#deployment-notes)
    - [Applications](#applications)
  - [Generated Docs](#generated-docs)
  - [Contributing](#contributing)
  - [License](#license)

## Features

- Utilizes C#, .NET 9, Dapr, GitHub Actions, Docker, Docker-Compose, and/or Kubernetes
- **WebSocket-First Architecture** with Connect service edge gateway for zero-copy message routing
- **Schema-Driven Development** with NSwag code generation from OpenAPI specifications
- **Dual-Transport Testing** supporting both HTTP and WebSocket protocols
- Works in conjunction with popular game engines like Unreal and Unity
- Provides built-in APIs for backend multiplayer video game support with binary protocol efficiency
- Easily scalable and maintainable with microservices architecture
- Complements the CelestialLink universal platform for online game development and hosting

## WebSocket-First Architecture

Bannou features an innovative **Connect service edge gateway** that enables zero-copy message routing and seamless dual-transport communication:

### Connect Service Edge Gateway
- **Service GUID Routing**: Messages routed via 16-byte service identifiers without payload inspection
- **Zero-Copy Performance**: Connect service never deserializes message contents for maximum efficiency
- **Client-Specific Security**: Same service receives different GUID per client connection (salted for security)
- **Progressive Access Control**: Service mappings dynamically update based on authentication state

### Binary Protocol
- **31-byte Header**: [Flags: 1][Channel: 2][Sequence: 4][Service GUID: 16][Message ID: 8]
- **Variable Payload**: JSON or binary data support with automatic serialization
- **Service Discovery**: Clients receive method → GUID mappings at connection time
- **Bidirectional RPC**: RabbitMQ integration enables server-initiated requests to clients

### Dual Routing Capability
- **Client-to-Client**: P2P communication using the same WebSocket protocol
- **Client-to-Service**: Traditional client-server patterns via Connect service routing
- **Additional Connections**: WebSocket negotiates separate TCP/UDP connections for specialized needs (low-latency input, streaming)

See [WEBSOCKET-PROTOCOL.md](WEBSOCKET-PROTOCOL.md) for complete technical documentation.

## Schema-Driven Development

Bannou uses **contract-first development** where OpenAPI specifications define the single source of truth for all APIs:

### NSwag Code Generation
- **YAML Schemas**: Define APIs in `/schemas/` directory using OpenAPI 3.0
- **Automatic Controllers**: Generate abstract controllers with full validation from schemas
- **TypeScript Clients**: Auto-generate game integration libraries from the same schemas
- **Model Generation**: Request/response models with proper validation attributes

### Benefits
- **API Consistency**: All services follow identical patterns derived from schemas
- **Type Safety**: Generated clients provide compile-time validation
- **Documentation**: Interactive Swagger UI with zero maintenance overhead
- **Validation**: Automatic request/response validation against contracts

### Schema-First Workflow
1. Define API contract in OpenAPI YAML
2. Generate controllers and models with NSwag
3. Implement business logic in service classes
4. Generated tests validate schema compliance
5. TypeScript clients enable type-safe game integration

See API-DESIGN.md (Technical Architecture knowledge base section) for detailed implementation guide.

### Development Workflow
After updating schemas or regenerating NSwag code:
```bash
# Regenerate controllers and models
nswag run

# Fix line endings for generated files (ensures EditorConfig compliance)
./fix-generated-line-endings.sh

# Verify lint compliance
docker run --rm -v $(pwd):/tmp/lint:rw oxsecurity/megalinter-dotnet:v8 -e "ENABLE=EDITORCONFIG"
```

## Testing & Development Commands

Bannou provides comprehensive testing via Makefile commands and automated CI/CD pipeline:

### Essential Makefile Commands

```bash
# Development Workflow
make build                      # Build all projects
make generate-all              # Regenerate all services and SDK
make generate-services         # Regenerate services only
make generate-services PLUGIN=accounts  # Regenerate specific service
make format                    # Fix line endings and run code formatting
make format-strict             # Enhanced formatting with CI EditorConfig validation
make lint-editorconfig         # Run exact CI EditorConfig validation (Docker)
make lint-editorconfig-fast    # Quick EditorConfig check (no Docker needed)
make validate                  # Complete pre-push validation (format + tests)
make clean                     # Clean all generated files
make clean PLUGIN=accounts     # Clean specific service

# Testing Commands
make test                      # Run all unit tests across all services
make test PLUGIN=accounts      # Run tests for specific service only
make test-ci                   # Complete CI pipeline (matches GitHub Actions)
make test-unit                 # Basic unit tests only
make test-http                 # HTTP endpoint testing
make test-edge                 # WebSocket protocol testing
make test-infrastructure       # Infrastructure validation

# Docker Compose
make build-compose             # Build Docker containers
make up-compose                # Start services locally
make down-compose              # Stop and cleanup
```

### Testing Architecture

Bannou implements a **comprehensive 10-step CI/CD pipeline** with dual-transport testing:

- **Schema-Driven Test Generation**: Automatic test creation from OpenAPI specifications
- **Dual-Transport Validation**: HTTP and WebSocket testing ensure protocol consistency
- **Service-Specific Testing**: Use `make test PLUGIN=service` for focused testing
- **CI/CD Integration**: Complete GitHub Actions pipeline with 400+ automated tests

**Quick Testing:**
```bash
make test                      # All tests (438 total across all services)
make test PLUGIN=accounts      # Specific service tests only
make test-ci                   # Full CI pipeline locally
```

See **[TESTING.md](TESTING.md)** for complete testing documentation, CI/CD pipeline details, and advanced testing workflows.

## Orchestrator Service

Bannou includes a **deployment orchestrator** for managing service topology and container lifecycle. The orchestrator provides API-driven control over environments, replacing manual Makefile/docker-compose commands with a unified programmatic interface.

### Key Capabilities

- **Multi-Backend Support**: Docker Compose, Docker Swarm, Kubernetes, Portainer
- **Preset-Based Deployment**: YAML topology definitions (7 presets included)
- **Standalone Dapr Sidecars**: Each app gets a paired Dapr container (not shared network namespace)
- **Health Monitoring**: Redis-based heartbeats for NGINX routing integration
- **Direct Infrastructure Access**: Bypasses Dapr for Redis/RabbitMQ (avoids chicken-and-egg dependency)

### Architecture Decisions

The orchestrator evolved through significant iteration to work around Dapr and Docker Compose limitations:

| Decision | Reason |
|----------|--------|
| **Standalone Dapr sidecars** | `network_mode:service` shared namespaces caused Dapr compatibility issues |
| **mDNS for Dapr discovery** | Consul was unnecessary - mDNS works natively on Docker bridge networks |
| **ExtraHosts IP injection** | Docker DNS (127.0.0.11) unreliable for dynamically created containers |
| **Direct Redis/RabbitMQ** | Orchestrator must start before Dapr infrastructure is available |

### Deployment Presets

Located in `provisioning/orchestrator/presets/`:

| Preset | Description |
|--------|-------------|
| `bannou.yaml` | Default monolith - all services on one node |
| `http-tests.yaml` | HTTP integration testing environment |
| `edge-tests.yaml` | WebSocket protocol testing environment |
| `auth-only.yaml` | Minimal auth service for testing |
| `minimal-services.yaml` | Core services only |
| `local-development.yaml` | Full development stack |
| `split-auth-routing-test.yaml` | Multi-node routing validation |

### Usage

```bash
# Start orchestrator with infrastructure
make up-orchestrator

# Deploy using a preset
curl -X POST http://localhost:5012/orchestrator/deploy \
  -H "Content-Type: application/json" \
  -d '{"preset": "http-tests"}'

# Check deployment status
curl http://localhost:5012/orchestrator/status
```

See [ORCHESTRATOR-SERVICE-DESIGN.md](docs/ORCHESTRATOR-SERVICE-DESIGN.md) for complete documentation.

## Local Deploy (Compose)

The service can be deployed locally (usually for initial testing purposes) with Docker-Compose / Docker for Desktop.

### Prerequisites

- Docker / Docker-Compose (Docker for Desktop)
- Make (optional)

### Manual

1. Clone this repository:

    `git clone https://github.com/ParnassianStudios/bannou-service.git`

2. To build, run the following, replacing `my_project` with your own project name:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project build`

3. To deploy locally (minimal service setup), run the following:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project up -d`

### Make

Alternatively, the following make commands have been provided to simplify the process. "cl" is used as a default project name with these.

1. `make build`
2. `make up -d`
3. `make down`

## How Plugins Work

Bannou uses a revolutionary schema-first plugin architecture that enables rapid service development and deployment flexibility. The entire system is designed around OpenAPI specifications that automatically generate complete, production-ready service plugins.

### Schema-First Development Workflow

**1. Define Your Service API**
Start by creating an OpenAPI specification in the `/schemas/` directory:

```yaml
# schemas/example-api.yaml
openapi: 3.0.1
info:
  title: Example Service API
  version: 1.0.0
paths:
  /example/hello:
    post:
      summary: Say hello
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/HelloRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HelloResponse'
components:
  schemas:
    HelloRequest:
      type: object
      properties:
        name:
          type: string
      required: [name]
    HelloResponse:
      type: object
      properties:
        message:
          type: string
      required: [message]
  x-service-configuration:
    properties:
      MaxConcurrentRequests:
        type: integer
        default: 100
      EnableCaching:
        type: boolean
        default: true
```

**2. Generate Complete Plugin Structure**
Run the code generation system to create your complete plugin:

```bash
scripts/generate-all-services.sh
```

This automatically creates:
```
lib-example/
├── Generated/                              # Auto-generated files (never edit manually)
│   ├── ExampleController.Generated.cs      # ASP.NET Core controller implementation
│   ├── IExampleService.cs                  # Service interface with method signatures
│   ├── ExampleClient.cs                    # Typed client for service-to-service calls
│   ├── ExampleModels.cs                    # Request/response/event model classes
│   └── ExampleServiceConfiguration.cs      # Configuration class from schema
├── ExampleService.cs                       # Your business logic implementation (only manual file)
├── ExampleServicePlugin.cs                 # Plugin registration and lifecycle
└── lib-example.csproj                      # Project file with dependencies
```

**3. Implement Business Logic**
The only file you need to edit is the service implementation. Everything else is generated:

```csharp
// ExampleService.cs - The ONLY manual file in your plugin
[DaprService("example", typeof(IExampleService), lifetime: ServiceLifetime.Scoped)]
public class ExampleService : IExampleService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ExampleService> _logger;
    private readonly ExampleServiceConfiguration _configuration;

    public ExampleService(
        DaprClient daprClient,
        ILogger<ExampleService> logger,
        ExampleServiceConfiguration configuration)
    {
        _daprClient = daprClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<(StatusCodes, HelloResponse?)> SayHelloAsync(
        HelloRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Saying hello to {Name}", request.Name);

            var response = new HelloResponse
            {
                Message = $"Hello, {request.Name}!"
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to say hello to {Name}", request.Name);
            return (StatusCodes.InternalServerError, null);
        }
    }
}
```

**4. Configure and Deploy**
Use environment variables to control which services run where:

```bash
# Enable your service
EXAMPLE_SERVICE_ENABLED=true

# Or disable all services except yours
SERVICES_ENABLED=false
EXAMPLE_SERVICE_ENABLED=true
```

### Plugin Architecture Benefits

**🚀 Rapid Development**
- New service from concept to production in under a day
- Generate controllers, models, clients, and configuration automatically
- Focus only on business logic, not infrastructure code

**⚙️ Selective Assembly Loading**
- Single codebase can run as monolith (all services) or microservices (selected services)
- Docker images built with only necessary service combinations
- Perfect for development (all local) to production (distributed) scaling

**🔌 Dynamic Service Discovery**
- Services automatically register with Dapr service mesh
- Client-to-service routing handled transparently through `ServiceAppMappingResolver`
- Default to "bannou" (omnipotent routing) for development, distributed routing for production

**🛡️ Type Safety & Consistency**
- Generated clients provide compile-time type checking for service-to-service calls
- OpenAPI schema ensures request/response consistency across all services
- Automatic input validation from schema definitions

**🧪 Comprehensive Testing**
- Unit tests auto-generated for all service methods
- HTTP integration testing through generated clients
- WebSocket protocol testing for Connect service integration

### Service Enable/Disable System

Bannou supports two-mode environment variable configuration for maximum flexibility:

**Global Control (SERVICES_ENABLED)**
```bash
# Enable all services by default
SERVICES_ENABLED=true    # Default behavior

# Disable all services by default (selective enabling)
SERVICES_ENABLED=false
EXAMPLE_SERVICE_ENABLED=true    # Only enable specific services
TESTING_SERVICE_ENABLED=true
```

**Individual Service Control**
```bash
# Individual service toggles (override global setting)
ACCOUNTS_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=false  # Disable specific services
```

This enables efficient resource usage and deployment flexibility:
- **Development**: All services enabled locally for full-stack development
- **Testing**: Minimal services (just testing service) for infrastructure validation
- **Production**: Distribute services across multiple nodes based on load patterns

### Integration with Dapr Service Mesh

Every plugin integrates seamlessly with Dapr for:

**Service-to-Service Communication**
```csharp
// Generated clients handle Dapr routing automatically
public class ExampleService : IExampleService
{
    private readonly IAccountsClient _accountsClient;  // Generated client

    public async Task<(StatusCodes, SomeResponse?)> DoSomethingAsync(SomeRequest request)
    {
        // Call other services using generated clients - routing handled automatically
        var (status, account) = await _accountsClient.GetAccountAsync(request.AccountId);
        if (status != StatusCodes.OK) return (status, null);

        // Your business logic here...
    }
}
```

**State Management and Events**
```csharp
// Dapr state store integration
private const string STATE_STORE = "example-store";
await _daprClient.SaveStateAsync(STATE_STORE, key, data);
var data = await _daprClient.GetStateAsync<ModelType>(STATE_STORE, key);

// Dapr pub/sub event publishing
await _daprClient.PublishEventAsync("bannou-pubsub", "example-event", eventModel);
```

**Configuration and Secrets**
```csharp
// Generated configuration classes with environment variable binding
public class ExampleServiceConfiguration : BaseServiceConfiguration
{
    [ServiceConfiguration(envPrefix: "BANNOU_")]
    public int MaxConcurrentRequests { get; set; } = 100;

    [ServiceConfiguration(envPrefix: "BANNOU_")]
    public bool EnableCaching { get; set; } = true;
}
```

### Advanced Plugin Features

**WebSocket Integration**
All services automatically integrate with the Connect service WebSocket gateway:
- Binary protocol with 31-byte headers for efficient routing
- Client-salted service GUIDs prevent cross-session security exploits
- Real-time capability updates when services deploy new APIs

**Assembly Loading Optimization**
```bash
# Build specialized Docker images with only required services
docker build --build-arg ENABLED_SERVICES="accounts,auth,connect" .

# Or build full-featured development image
docker build --build-arg ENABLED_SERVICES="all" .
```

**Multi-Environment Configuration**
```bash
# Development environment (.env)
BANNOU_EmulateDapr=True
SERVICES_ENABLED=true

# Production environment
DAPR_HTTP_PORT=3500
DAPR_GRPC_PORT=50001
SERVICES_ENABLED=false
ACCOUNTS_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
```

This plugin architecture enables Bannou to scale from single-developer local development to massive distributed deployments supporting hundreds of thousands of concurrent NPCs, all from the same codebase and deployment tooling.

## Extending the Service

With the schema-first plugin architecture, extending Bannou is straightforward and follows proven patterns. The generated code handles all infrastructure concerns, letting you focus on business value.

### Adding a New Service

The complete workflow from idea to production deployment:

1. **Create OpenAPI Schema** (`/schemas/{service}-api.yaml`)
   - Define your service API contract with request/response models
   - Include configuration schema in `x-service-configuration` section
   - Use standard HTTP status codes and error handling patterns

2. **Generate Plugin Structure** (`scripts/generate-all-services.sh`)
   - Creates complete plugin with controllers, models, clients, configuration
   - Generates project files and dependency references automatically
   - Updates solution file with new project references

3. **Implement Service Logic** (Edit only `{Service}Service.cs`)
   - Implement generated interface with your business logic
   - Use DaprClient for state management and service communication
   - Follow `(StatusCodes, ResponseModel?)` tuple pattern for responses

4. **Configure and Test** (Environment variables and testing)
   - Enable service with `{SERVICE}_SERVICE_ENABLED=true`
   - Run generated unit tests and integration tests
   - Test service-to-service communication using generated clients

5. **Deploy and Scale** (Docker and service mesh)
   - Service automatically registers with Dapr service discovery
   - Configure selective assembly loading for production deployment
   - Monitor and scale using standard Dapr metrics and tooling

The plugin system ensures consistency, type safety, and rapid development while maintaining production-grade reliability and performance.

### Special Case: Testing Service Plugin

The `lib-testing` plugin serves as an example of a manually-created plugin that follows the same architectural patterns as generated plugins, but is hand-crafted for infrastructure testing purposes:

```
lib-testing/
├── TestingService.cs              # Business logic (implements ITestingService interface)
├── TestingServiceConfiguration.cs # Configuration (extends BaseServiceConfiguration)
├── TestingController.cs           # Controller (follows same patterns as generated controllers)
├── ITestingService.cs            # Service interface (manually defined)
└── lib-testing.csproj            # Project file
```

This plugin demonstrates how to create services that don't require schema generation but still integrate seamlessly with the plugin system, service discovery, and configuration management.

## Deployment Notes

Deploying the monoservice can be handled in number number of ways, depending on the specific requirements. We'll add a section soon with example projects which outline the entire process taking different paths with fresh installs. In the meantime, there are some notes below to keep in mind.

### Applications

Each group of services that need to be routed and scaled independently in the deployment environment are referred to as "applications"/"apps". Common applications might be login, queue, and account management in one place, or an app which handle various types of assets, like images, audio, and video via different controllers. While this logic might be spread across several services and/or API controllers, they might use much of the same backend support or have other commonalities in which it makes sense to group and scale them together. The concept of applications make that process much easier.

Application setups might be:
1. *Every* service type being given its own application so that they can be scaled separately.
2. Some services/controllers making up one application, while all of the rest are also grouped apart from it.
3. One application for all services/controllers, scaled across any number of instances/nodes (all APIs scale equally).
4. One instance of a single app handling all APIs and performing all tasks (local dev / single node).

You have complete control over how complex your deployment environment needs to be.

Applications are assigned via Dapr configuration, which means they can be updated while running so that certain apps (service groups) can handle more or less responsibilities at any given time depending on the network state, as failovers, for transitions, etc. However, keep in mind that an individual monoservice instance's ***capabilities*** can't be changed dynamically, without restarting the service stack. Service classes and controllers are set up during program start based on ENVs, and make their connections to various backend databases and such at that time- even if you were to change the "login" application to suddenly handle something new, like the leaderboard APIs, it wouldn't be able to actually do so unless the leaderboard service class/controller had already been enabled on start. It needs that internal set of services and API controllers actually enabled to use them.

The separation between enabling services/controllers (via ENVs) and mapping services to applications (Dapr configuration) might be a bit hard to wrap your head around at first, but it's a key piece of what makes this monoservice so versatile, regardless of your project's requirements.

## Generated Docs

- [WebSocket Protocol Architecture](WEBSOCKET-PROTOCOL.md) - Complete binary protocol specification
- API Design & Schema-Driven Development (Technical Architecture knowledge base) - Contract-first development guide
- [Testing Architecture](TESTING.md) - Dual-transport and schema-driven testing
- [EditorConfig and Linting Guide](LINTING.md) - CI-compatible formatting and validation
- [Service Configuration](documentation/configuration.md) - Environment and deployment configuration
- [Service APIs](documentation/services.md) - Generated API documentation

## Contributing

If you would like to contribute to the Bannou Service project, please follow the [contributing guidelines](documentation/CONTRIBUTING.md).

## License

This project is licensed under the [MIT License](LICENSE).
