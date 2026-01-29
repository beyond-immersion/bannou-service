# Bannou Development Tools

This directory contains development and testing tools for the Bannou service platform.

## Tools Overview

| Tool | Purpose |
|------|---------|
| `http-tester` | HTTP integration testing - simulates service-to-service calls |
| `edge-tester` | WebSocket edge testing - validates binary protocol and client SDK |
| `bannou-inspect` | Assembly inspector - IntelliSense-like type/method inspection from CLI |

## Building

Build all tools:
```bash
dotnet build tools/tools.sln
```

Or use the Makefile:
```bash
make build-tools
```

## bannou-inspect

A command-line tool for inspecting types, methods, and documentation from NuGet packages or local assemblies. Useful for understanding external APIs without leaving the terminal.

### Commands

#### `type` - Inspect a type's signature and documentation
```bash
# Inspect a type from a NuGet package
dotnet run --project tools/bannou-inspect -- type IChannel --package RabbitMQ.Client

# Inspect a type from a local assembly
dotnet run --project tools/bannou-inspect -- type MyType --assembly ./bin/Debug/net9.0/MyApp.dll

# Specify a package version
dotnet run --project tools/bannou-inspect -- type IChannel --package RabbitMQ.Client --version 7.0.0
```

#### `method` - Inspect a method with parameters and exceptions
```bash
# Inspect a method
dotnet run --project tools/bannou-inspect -- method "IChannel.BasicPublishAsync" --package RabbitMQ.Client

# Or specify type separately
dotnet run --project tools/bannou-inspect -- method "BasicPublishAsync" --type IChannel --package RabbitMQ.Client
```

#### `list-types` - List all public types in an assembly
```bash
dotnet run --project tools/bannou-inspect -- list-types --package RabbitMQ.Client
```

#### `search` - Search for types by name pattern
```bash
# Search with wildcards
dotnet run --project tools/bannou-inspect -- search "*Connection*" --package RabbitMQ.Client
```

### Makefile Shortcuts

```bash
make inspect-type TYPE="IChannel" PKG="RabbitMQ.Client"
make inspect-method METHOD="IChannel.BasicPublishAsync" PKG="RabbitMQ.Client"
make inspect-search PATTERN="*Connection*" PKG="RabbitMQ.Client"
```

## http-tester

HTTP integration tester that validates service-to-service communication via lib-mesh. Used during `make test-http`.

Features:
- Uses generated NSwag clients for type-safe API calls
- Tests all service endpoints via HTTP
- Validates response schemas and status codes
- Runs as a Docker container alongside the service stack

## edge-tester

WebSocket edge tester that validates the binary protocol and TypeScript SDK. Used during `make test-edge`.

Features:
- Tests WebSocket connection lifecycle
- Validates binary message framing (31-byte header)
- Tests client-salted GUID routing
- Includes TypeScript SDK harness for cross-SDK validation
- Tests reconnection and session recovery

## License

All tools are MIT licensed as part of the Bannou project.
