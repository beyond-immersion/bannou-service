# Bannou Service

[![Build Status](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.unit.yml/badge.svg?branch=master&event=push)](https://github.com/beyond-immersion/bannou-service/actions/workflows/ci.unit.yml)

Bannou is a composable game platform -- 76 schema-driven service primitives that combine to create game systems like economies, crafting, NPC behavior, and narrative without per-feature backend code. It deploys identically as a cloud service, a self-hosted sidecar, or embedded in-process, so the same SDK powers a mobile single-player game and a 100,000-player MMO. Alongside the service runtime, a family of pure-computation creative SDKs generate music, narratives, and NPC behaviors procedurally using GOAP planning and formal academic theory -- not AI/LLM inference. All code is generated from OpenAPI schemas; developers write behavior documents (ABML), seed data, and visual layers, not systems programming.

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

**Next steps:** See the [Quickstart Guide](docs/guides/QUICKSTART.md) for client/service integration, or the [Getting Started Guide](docs/guides/GETTING-STARTED.md) for a comprehensive walkthrough.

## Documentation

### Getting Started

| I want to... | Time | Guide |
|--------------|------|-------|
| **Get running quickly** | 5 min | [Quickstart](docs/guides/QUICKSTART.md) |
| **Full setup walkthrough** | 30 min | [Getting Started](docs/guides/GETTING-STARTED.md) |
| **Connect a game client** | 15 min | [Client Integration](docs/guides/CLIENT-INTEGRATION.md) |
| **Make service-to-service calls** | 10 min | [SDKs Overview](docs/guides/SDK-OVERVIEW.md) |

### Development

| I want to... | Read... |
|--------------|---------|
| Understand the architecture | [Bannou Design](docs/BANNOU-DESIGN.md) |
| Add or extend a plugin | [Plugin Development Guide](docs/guides/PLUGIN-DEVELOPMENT.md) |
| Understand a specific service | [Plugin Deep-Dives](docs/plugins/) (76+ services) |
| Run and write tests | [Testing Guide](docs/operations/TESTING.md) |
| Contribute code | [Development Rules](docs/reference/TENETS.md) |

### Operations

| I want to... | Read... |
|--------------|---------|
| Deploy to production | [Deployment Guide](docs/guides/DEPLOYMENT.md) |
| Understand CI/CD pipelines | [GitHub Actions](docs/operations/GITHUB-ACTIONS.md) |
| Set up NuGet publishing | [NuGet Setup](docs/operations/NUGET-SETUP.md) |

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
- [Schema Rules](docs/reference/SCHEMA-RULES.md) - OpenAPI schema authoring reference
- [Permissions System](docs/X-PERMISSIONS-SPECIFICATION.md) - Role-based access control schema
- [Service Details](docs/GENERATED-SERVICE-DETAILS.md) - Service descriptions and API endpoints
- [Events Reference](docs/GENERATED-EVENTS.md) - Auto-generated event documentation
- [Configuration Reference](docs/GENERATED-CONFIGURATION.md) - Environment variables
- [State Stores](docs/GENERATED-STATE-STORES.md) - Redis/MySQL state stores

## Project Structure

```
bannou-service/
├── schemas/              # OpenAPI specifications (source of truth)
├── plugins/lib-*/        # Service plugins (76+ services)
├── bannou-service/       # Main application and shared code
├── sdks/                 # SDK packages (C#, TypeScript, Unreal)
│   ├── core/             # Shared types (BannouJson, ApiException, base events)
│   ├── server/           # Server SDK (mesh clients, behavior runtime)
│   ├── client/           # Client SDK (WebSocket, typed proxies, events)
│   ├── client-voice/     # Voice communication SDK
│   ├── bundle-format/    # .bannou archive format (LZ4 compression)
│   ├── asset-*/          # Asset loading/bundling (client, server, Stride, Godot)
│   ├── scene-composer-*/ # Scene composition (core, Stride, Godot)
│   ├── music-*/          # Music generation (theory, storyteller)
│   ├── typescript/       # TypeScript client SDK
│   └── unreal/           # Unreal Engine integration
├── tools/                # Testing and inspection tools
│   ├── http-tester/      # HTTP integration test framework
│   ├── edge-tester/      # WebSocket edge test framework
│   └── bannou-inspect/   # Assembly inspection CLI
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
