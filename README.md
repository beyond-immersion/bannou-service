# Bannou Service

[![Build Status](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.integration.yml/badge.svg?branch=master&event=push)](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.integration.yml)

Bannou is a schema-driven monoservice platform for multiplayer games. It provides a WebSocket-first edge gateway with zero-copy message routing, plugin-based service architecture, and abstracted infrastructure (lib-state, lib-messaging, lib-mesh). Designed to support Arcadia, a revolutionary MMORPG with 100,000+ AI-driven NPCs, Bannou scales from a single development machine to distributed production clusters without code changes.

## Quick Start

```bash
# 1. Clone
git clone https://github.com/beyond-immersion/bannou-service.git
cd bannou-service

# 2. Install dev tools (.NET 10, NSwag, Python, Node.js)
./scripts/install-dev-tools.sh
source ~/.bashrc

# 3. Build and verify
make quick

# 4. Start the stack
make up-compose

# 5. Verify it's running
curl http://localhost:8080/health
```

**Next steps:** See the [Quickstart Guide](docs/guides/QUICKSTART.md) for client/service integration, or the [Getting Started Guide](docs/guides/GETTING_STARTED.md) for a comprehensive walkthrough.

## Documentation

### Getting Started

| I want to... | Time | Guide |
|--------------|------|-------|
| **Get running quickly** | 5 min | [Quickstart](docs/guides/QUICKSTART.md) |
| **Full setup walkthrough** | 30 min | [Getting Started](docs/guides/GETTING_STARTED.md) |
| **Connect a game client** | 15 min | [Client Integration](docs/guides/CLIENT_INTEGRATION.md) |
| **Make service-to-service calls** | 10 min | [SDKs Overview](docs/guides/SDKs.md) |

### Development

| I want to... | Read... |
|--------------|---------|
| Understand the architecture | [Bannou Design](docs/BANNOU_DESIGN.md) |
| Add or extend a plugin | [Plugin Development Guide](docs/guides/PLUGIN_DEVELOPMENT.md) |
| Run and write tests | [Testing Guide](docs/guides/TESTING.md) |
| Contribute code | [Development Rules](docs/reference/TENETS.md) |

### Operations

| I want to... | Read... |
|--------------|---------|
| Deploy to production | [Deployment Guide](docs/guides/DEPLOYMENT.md) |
| Understand CI/CD pipelines | [GitHub Actions](docs/operations/GITHUB_ACTIONS.md) |
| Set up NuGet publishing | [NuGet Setup](docs/operations/NUGET_SETUP.md) |

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
├── plugins/lib-*/        # Service plugins (33 services)
├── bannou-service/       # Main application and shared code
├── sdks/                 # SDK packages
│   ├── core/             # Shared types (BannouJson, ApiException, base events)
│   ├── server/           # Server SDK (mesh clients, behavior runtime)
│   ├── client/           # Client SDK (WebSocket, behavior)
│   ├── client-voice/     # Voice communication SDK
│   ├── protocol/         # UDP game state protocol
│   ├── transport/        # LiteNetLib transport layer
│   ├── scene-composer/   # Scene composition (engine-agnostic)
│   ├── scene-composer-stride/  # Stride engine bridge
│   └── scene-composer-godot/   # Godot engine bridge
├── examples/             # Example projects and demos
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
