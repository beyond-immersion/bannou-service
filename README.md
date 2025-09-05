# Bannou Service

[![Build Status](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml/badge.svg?branch=master&event=push)](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml)

Bannou Service is a versatile ASP.NET Core application designed to provide a WebSocket-first microservices architecture for massively multiplayer online games. Featuring an intelligent Connect service edge gateway that routes messages via service GUIDs without payload inspection, Bannou enables zero-copy message routing and seamless dual-transport communication (HTTP for development, WebSocket for production). The platform uses schema-driven development with NSwag code generation to ensure API consistency across all services. Primarily designed to support Arcadia, a revolutionary MMORPG with AI-driven NPCs, Bannou becomes the foundation of the universal cloud-based platform for developing and hosting multiplayer video games, tentatively called "CelestialLink".

## Table of Contents

- [Bannou Service](#bannou-service)
  - [Table of Contents](#table-of-contents)
  - [Features](#features)
  - [WebSocket-First Architecture](#websocket-first-architecture)
  - [Schema-Driven Development](#schema-driven-development)
  - [Testing Architecture](#testing-architecture)
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
- **24-byte Header**: 16-byte service GUID + 8-byte message ID for correlation
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

See [API-DESIGN.md](API-DESIGN.md) for detailed implementation guide.

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

## Testing Architecture

Bannou implements a comprehensive **dual-transport testing** system with automatic test generation:

### Schema-Driven Test Generation
- **Automatic Coverage**: Tests generated for success, validation, and authorization scenarios
- **OpenAPI Integration**: YAML schemas drive comprehensive test case creation
- **Failure Scenarios**: Missing required fields, invalid types, unauthorized access automatically tested

### Dual-Transport Validation
- **HTTP Testing**: Direct service endpoint validation (development/debugging)
- **WebSocket Testing**: Complete Connect service protocol validation (production experience)
- **Consistency Verification**: Same tests run via both transports to ensure identical behavior
- **Transport Discrepancy Detection**: Identifies inconsistencies between HTTP and WebSocket responses

### Testing Clients
- **Shared Interface**: `ITestClient` abstraction works with both HTTP and WebSocket
- **Schema Integration**: `ISchemaTestHandler` enables automatic test generation from OpenAPI specs
- **Comprehensive Reporting**: Test results include transport comparison and discrepancy analysis

Run tests via:
```bash
# HTTP direct testing
dotnet run --project http-tester

# WebSocket protocol testing  
dotnet run --project edge-tester

# All unit tests (167 total)
dotnet test
```

See [TESTING.md](TESTING.md) for complete testing documentation.

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

## Extending the Service

The Bannou Service's primary goal is to be flexible, so there are numerous ways to use and extend it. The classic use would be to fork this repository, adding your own APIs (see below on adding APIs) directly to the main application in the same way that the existing services, controllers, and configuration are set up. The application code itself is your guide- MVC controllers are still the same MVC controllers you'd always deal with in .NET 7, and should be fairly self-explanatory.

Alternatively, you can add this repository as a project reference / git submodule of your own .NET application. The Program class in this monoservice has been kept intentionally minimalistic, and all mechanisms of service, controller, and configuration discovery have been written in a way to also include other loaded assemblies. Until more examples can be included, the `unit tests` project clearly shows that extending the base attribute, configuration, service, and controller classes works just fine when using the Bannou Service as a project reference.

Finally, you can build this application using the dotnet commandline build tool, and reference/include in the generated library in your own app.

### Adding APIs

To add an API controller, first determine if the APIs are distinct enough to potentially have their own on/off switch enabling them for a given service instance. In other words, would you want only some nodes in your services network to have these APIs enabled, while others have them disabled?

If you need that control, then you'll want to start by implementing IDaprService in a new "service handler" class- this new class is where you'll do your initial setup in support of the API controller you'll be adding. It gives you the on/off switch to enable and disable the controllers by configuration, as well as handling long-running tasks for maintenance, cleanup, or generating events of some kind.

If your new APIs are generic enough that you don't mind requests being spread across every node in the network without that level of control, you can forego having a "service handler" entirely, and move right to adding the Controller.

### Implementing IDaprService

Dapr services are the classes which support the API controllers, by providing the business logic of transactional requests, an entry point for starting long-running service tasks, the cleanup handler when the service is shutting down, and the means by which easy per-controller configuration can be generated.

There are two requirements for adding a new "service handler" to the application- one is implementing the IDaprService interface, and the second is decorating the class with the `[DaprServiceAttribute]`. The attribute allows you to specify a service handler name- this should be unique per Dapr service, as it's used to determine the ENV for enabling and disabling the service handler (and its associated API controllers).

Your individual Dapr service can be enabled by setting `{SERVICE_NAME}_SERVICE_ENABLED=true` as an ENV or `--{service_name}-service-enabled=true` as a switch.

### Implementing IServiceConfiguration

Configuration is meant to be per-Dapr service, so there are several ways through the service and associated controllers that configuration can be retrieved in the application. To add a new configuration class, implement the IServiceConfiguration interface and decorate it with the `[ServiceConfiguration]` attribute pointing to the particular Dapr service that the configuration is meant for. Any existing ENVs (and args/switches, if you provide them) will be used to populate your configuration class automatically. By adding a "prefix" param to the config attribute, you can specify that only ENVs with a given prefix should be used to populate it instead (a common mechanism for per-component configuration in a .NET application).

By decorating any configuration property in your new class with `[Required]` (from DataAnnotations), your service will then fail to start if the configuration is NOT provided. This is only when the Dapr service is enabled on that node- if it's disabled, then its required configuration obviously doesn't matter.

Dapr services can have any number of configuration classes without issue, but if you have more than one, then you should set `primary=true` in the attribute constructor for the one you wish to be treated as the service's primary configuration. This means the configuration is selected and populated automatically when building from the direction of the Dapr service or associated controller (as if it were the only configuration). Properties set with the `[Required]` attribute in classes that are NOT the primary configuration for a service type will be ignored.

### Implementing IDaprController

To add a new API controller to use through Dapr, implement IDaprController and decorate the class with the `[DaprController]` attribute. The attribute extends the `[Route]` attribute, so can be thought of similarly- add a template string for the route your controller will use, and each method path will be appended to that route.

If your API controller has business logic then you should also add a Dapr service class (see above) to handle that logic, and reference said Dapr service from the attribute decorating your controller. A Dapr service can have any number of associated controllers (meaning, any number of controllers can reference back to and use the service), but the opposite is not true- Dapr controllers can only reference one Dapr service each.

If you need to make a request from one Dapr service to another, keep in mind that the 2nd service may be disabled on your particular node at some point in the future, leading to problems. You MUST make such requests back out through Dapr to an internal controller, and not attempt to bypass that step- this is to prevent issues with concurrency, as well as simply keeping internal depencencies to a minimum.

### Implementing IServiceAttribute

IServiceAttribute is a shared interface for custom attributes within the application. The interface largely provides a set of utility methods, useful for finding all instances of the attribute decorating targets within the application (classes, methods, fields, etc).

The `[DaprService]`, `[DaprController]`, and `[ServiceConfiguration]` attributes all implement IServiceAttribute, and those same helper methods are what are used to locate and perform operations on all of the classes decorated with those attributes, so they can be used as examples of how those methods work.

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
- [API Design & Schema-Driven Development](API-DESIGN.md) - Contract-first development guide
- [Testing Architecture](TESTING.md) - Dual-transport and schema-driven testing
- [Service Configuration](documentation/configuration.md) - Environment and deployment configuration
- [Service APIs](documentation/services.md) - Generated API documentation

## Contributing

If you would like to contribute to the Bannou Service project, please follow the [contributing guidelines](documentation/CONTRIBUTING.md).

## License

This project is licensed under the [MIT License](LICENSE).
