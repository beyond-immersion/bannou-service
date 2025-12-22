# Bannou Service

[![Build Status](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml/badge.svg?branch=master&event=push)](https://github.com/ParnassianStudios/bannou-service/actions/workflows/ci.integration.yml)

Bannou is a schema-driven monoservice platform for multiplayer games. It provides a WebSocket-first edge gateway with zero-copy message routing, plugin-based service architecture, and Dapr integration for infrastructure portability. Designed to support Arcadia, a revolutionary MMORPG with 100,000+ AI-driven NPCs, Bannou scales from a single development machine to distributed production clusters without code changes.

## Quick Start

```bash
# Clone and start
git clone https://github.com/ParnassianStudios/bannou-service.git
cd bannou-service
make up-compose

# Run tests
make test                  # Unit tests (438 total)
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
| Contribute code | [Development Rules](docs/reference/TENETS.md) |

## Key Features

- **WebSocket-First**: Connect service edge gateway with 31-byte binary headers for zero-copy routing
- **Schema-Driven**: OpenAPI specs generate controllers, models, clients, and tests automatically
- **Plugin Architecture**: Each service is an independent assembly, loadable via environment config
- **Dapr Integration**: Portable infrastructure (databases, messaging) via Dapr components
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
- [Events Reference](docs/reference/GENERATED-EVENTS.md) - Auto-generated event documentation
- [State Stores](docs/reference/GENERATED-STATE-STORES.md) - Dapr component reference
- [Configuration Reference](docs/reference/CONFIGURATION.md) - Environment variables

## Project Structure

```
bannou-service/
├── schemas/              # OpenAPI specifications (source of truth)
├── lib-*/                # Service plugins (one per service)
├── bannou-service/       # Main application and shared code
├── docs/                 # Documentation
│   ├── guides/           # How-to guides
│   ├── reference/        # Technical reference
│   └── operations/       # CI/CD and infrastructure
├── provisioning/         # Docker, Dapr, and deployment configs
└── scripts/              # Code generation and build scripts
```

## License

This project is licensed under the [MIT License](docs/LICENSE).
