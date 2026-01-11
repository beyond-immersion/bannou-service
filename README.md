# Bannou Service

[![Build Status](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.integration.yml/badge.svg?branch=master&event=push)](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.integration.yml)

Bannou is a schema-driven monoservice platform for multiplayer games. It provides a WebSocket-first edge gateway with zero-copy message routing, plugin-based service architecture, and abstracted infrastructure (lib-state, lib-messaging, lib-mesh). Designed to support Arcadia, a revolutionary MMORPG with 100,000+ AI-driven NPCs, Bannou scales from a single development machine to distributed production clusters without code changes.

## Quick Start

```bash
# Clone and start
git clone https://github.com/beyond-immersion/bannou-service.git
cd bannou-service
make up-compose

# Run tests
make test                  # Unit tests (3,300+ total)
make test-http             # HTTP integration tests
make test-edge             # WebSocket protocol tests
```

## Documentation

| I want to... | Read... |
|--------------|---------|
| Understand the architecture | [Bannou Design](docs/BANNOU_DESIGN.md) |
| Add or extend a plugin | [Plugin Development Guide](docs/guides/PLUGIN_DEVELOPMENT.md) |
| Deploy to production | [Deployment Guide](docs/guides/DEPLOYMENT.md) |
| Integrate a game client | [Client Integration](docs/guides/CLIENT_INTEGRATION.md) |
| Run and write tests | [Testing Guide](docs/guides/TESTING.md) |
| Understand CI/CD pipelines | [GitHub Actions](docs/operations/GITHUB_ACTIONS.md) |
| Set up NuGet publishing | [NuGet Setup](docs/operations/NUGET_SETUP.md) |
| Use the SDKs | [SDKs Overview](docs/guides/SDKs.md) |
| Contribute code | [Development Rules](docs/reference/TENETS.md) |

## Key Features

- **WebSocket-First**: Connect service edge gateway with 31-byte binary headers for zero-copy routing
- **Schema-Driven**: OpenAPI specs generate controllers, models, clients, and tests—you write only 18-35% of the code
- **Plugin Architecture**: Each service is an independent assembly, loadable via environment config
- **Infrastructure Abstraction**: Portable infrastructure (databases, messaging, service mesh) via lib-state, lib-messaging, and lib-mesh
- **Monoservice Flexibility**: Same binary deploys as monolith or distributed microservices

## Essential Commands

```bash
# Development
make build                 # Build all projects
make generate              # Regenerate services from schemas
make format                # Fix formatting and line endings

# Testing
make test                  # All unit tests
make test-http             # HTTP integration tests
make test-edge             # WebSocket edge tests
make test-ci               # Full CI pipeline locally

# Docker
make up-compose            # Start local stack
make down-compose          # Stop and cleanup
```

## Technical Specifications

- [WebSocket Protocol](docs/WEBSOCKET-PROTOCOL.md) - Binary protocol specification
- [Permissions System](docs/X-PERMISSIONS-SPECIFICATION.md) - Role-based access control schema
- [Service Details](docs/GENERATED-SERVICE-DETAILS.md) - Service descriptions and API endpoints
- [Events Reference](docs/GENERATED-EVENTS.md) - Auto-generated event documentation
- [Configuration Reference](docs/GENERATED-CONFIGURATION.md) - Environment variables
- [State Stores](docs/GENERATED-STATE-STORES.md) - Redis/MySQL state stores

## Project Structure

```
bannou-service/
├── schemas/              # OpenAPI specifications (source of truth)
├── plugins/lib-*/        # Service plugins (32 services)
├── bannou-service/       # Main application and shared code
├── Bannou.SDK/           # Server SDK (mesh clients, behavior runtime)
├── Bannou.Client.SDK/    # Game client SDK (WebSocket, behavior)
├── GameProtocol/         # UDP game state protocol
├── GameTransport/        # LiteNetLib transport layer
├── docs/                 # Documentation
├── provisioning/         # Docker and deployment configs
└── scripts/              # Code generation and build scripts
```

## Community

- **Discord**: [Beyond Immersion](https://discord.gg/3eAGYwF3rE) - Discussion, questions, and collaboration
- **GitHub Issues**: Bug reports and feature requests
- **GitHub Discussions**: Questions and broader conversations

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

Please note that this project follows a [Code of Conduct](CODE_OF_CONDUCT.md).

## License

This project is licensed under the [MIT License](LICENSE).
