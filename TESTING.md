# Bannou Testing Documentation

Comprehensive testing documentation for Bannou's schema-driven microservices architecture with WebSocket-first edge gateway and CI/CD integration.

## Overview

Bannou implements a **revolutionary 10-step CI/CD pipeline** with comprehensive testing coverage including:

- **Schema-Driven Development**: OpenAPI specifications drive automatic test generation
- **Service-Specific Testing**: Individual service plugin testing with `make test PLUGIN=service`
- **Dual-Transport Validation**: HTTP and WebSocket protocol consistency testing
- **GitHub Actions Integration**: Complete 10-step automated pipeline
- **189+ Automated Tests**: Unit tests, integration tests, and protocol validation

## Quick Start

```bash
# Test everything
make test                      # All 189+ tests across all services

# Test specific services
make test PLUGIN=accounts      # Test accounts service only
make test PLUGIN=auth          # Test auth service only
make test PLUGIN=connect       # Test connect service only

# Complete CI pipeline locally
make test-ci                   # Matches GitHub Actions 10-step pipeline

# Individual test types
make test-unit                 # Unit tests only
make test-http                 # HTTP endpoint testing
make test-edge                 # WebSocket protocol testing
make test-infrastructure       # Infrastructure validation
```

## Testing Architecture

### 1. Unit Tests (189+ Total)

**Service Test Structure**: Each service has comprehensive unit tests in `lib-{service}.tests/`:

```
lib-accounts.tests/           # Accounts service tests
lib-auth.tests/              # Authentication service tests
lib-behavior.tests/          # Behavior service tests
lib-connect.tests/           # Connect service tests
lib-game-session.tests/      # Game session tests
lib-permissions.tests/       # Permissions service tests
lib-website.tests/           # Website service tests
unit-tests/                  # Core framework tests (155 tests)
```

**Generated Test Structure**: Tests follow xUnit patterns with proper dependency injection:

```csharp
public class AccountsServiceTests
{
    private Mock<ILogger<AccountsService>> _mockLogger = null!;
    private Mock<AccountsServiceConfiguration> _mockConfiguration = null!;

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        var service = new AccountsService(_mockLogger.Object, _mockConfiguration.Object);
        Assert.NotNull(service);
    }
}
```

### 2. HTTP Integration Testing (`http-tester`)

**Purpose**: Direct HTTP endpoint validation for service logic testing
**Location**: `http-tester/` project

**Features**:
- Direct service-to-service communication using generated clients
- JWT authentication workflows
- Comprehensive API endpoint validation
- Request/response model validation
- Service integration testing

**Usage**:
```bash
# Interactive mode
dotnet run --project http-tester

# CI/CD daemon mode
DAEMON_MODE=true dotnet run --project http-tester --configuration Release
```

### 3. WebSocket Protocol Testing (`edge-tester`)

**Purpose**: Complete Connect service edge gateway and binary protocol validation
**Location**: `edge-tester/` project

**Features**:
- WebSocket-first architecture validation
- 31-byte binary protocol header testing
- Service GUID routing validation
- Real-time capability updates
- Client-server protocol consistency
- Backwards/Forward compatibility testing

**Usage**:
```bash
# Interactive mode
dotnet run --project edge-tester

# CI/CD daemon mode
DAEMON_MODE=true dotnet run --project edge-tester --configuration Release
```

### 4. Infrastructure Testing

**Purpose**: Docker Compose service availability and connectivity validation
**Components**: OpenResty, Redis, RabbitMQ, MySQL basic connectivity

**Usage**:
```bash
# Standalone infrastructure testing
make test-infrastructure

# Full infrastructure with Docker Compose
make build-compose
make ci-up-compose
make test-infrastructure
```

## GitHub Actions 10-Step CI/CD Pipeline

### Pipeline Overview

The comprehensive CI/CD pipeline (`.github/workflows/ci.integration.yml`) ensures complete validation:

#### Generation and Build Phase
1. **🔧 Generate Services (Initial)** - Create controllers/models from OpenAPI schemas
2. **📦 Generate Client SDK (Initial)** - Build client SDK from service definitions
3. **🏗️ Build All Projects** - Compile with Release configuration

#### Consistency Validation Phase
4. **🔍 Generate Services (Conflict Detection)** - Re-generate to detect conflicts
5. **🔄 Generate Client SDK (Consistency Check)** - Ensure SDK consistency

#### Testing Phase
6. **🧪 Unit Tests** - 189+ tests across all service plugins
7. **🏗️ Infrastructure Tests** - Docker Compose + connectivity validation
8. **🔗 HTTP Integration Tests** - Service-to-service communication validation

#### Protocol Validation Phase
9. **📡 WebSocket Backwards Compatibility** - Published NuGet SDK compatibility
10. **🚀 WebSocket Forward Compatibility** - Current generated SDK compatibility

### Local Pipeline Reproduction

```bash
# Complete 10-step pipeline locally (matches GitHub Actions exactly)
make test-ci

# Individual pipeline components
make generate-services-for-consistency  # Steps 4-5
make test-unit                          # Step 6
make test-infrastructure                # Step 7
make test-http-daemon                   # Step 8
make test-edge-daemon                   # Steps 9-10
```

### Service-Specific Development Workflow

```bash
# Develop and test specific services
make clean PLUGIN=accounts             # Clean accounts service only
make generate-services PLUGIN=accounts # Generate accounts service only
make test PLUGIN=accounts              # Test accounts service only

# Complete service development cycle
make clean PLUGIN=behavior
make generate-services PLUGIN=behavior
make build
make test PLUGIN=behavior
```

## Schema-Driven Development Testing

### OpenAPI Schema Integration

**Schema Location**: `/schemas/` directory contains all service API definitions
**Generated Components**: Controllers, models, clients, interfaces, configurations

**Example Schema Testing Flow**:
1. Define API in `schemas/accounts-api.yaml`
2. Generate service with `make generate-services PLUGIN=accounts`
3. Implement business logic in `AccountsService.cs`
4. Test with `make test PLUGIN=accounts`
5. Validate via `make test-http` and `make test-edge`

### Automatic Test Generation

Tests are automatically created for each service plugin via `scripts/generate-tests.sh`:

**Generated Test Types**:
- Constructor validation tests
- Service configuration tests
- Basic functionality tests
- Integration test scaffolding

**Test Project Structure**:
```
lib-{service}.tests/
├── GlobalUsings.cs              # xUnit global imports
├── {Service}ServiceTests.cs     # Main service tests
└── lib-{service}.tests.csproj   # Test project file
```

## Advanced Testing Features

### WebSocket Binary Protocol

**Protocol Specification**: 31-byte binary header + JSON payload
- **Message Flags**: 1 byte - routing control
- **Channel**: 2 bytes - service channel identification
- **Sequence**: 4 bytes - message ordering
- **Service GUID**: 16 bytes - client-specific service routing
- **Message ID**: 8 bytes - request/response correlation

**Testing Implementation**: `edge-tester` validates complete protocol compliance including:
- Client-specific GUID generation (SHA256 salted)
- Zero-copy message routing via Connect service
- Real-time capability updates via RabbitMQ
- Service discovery and dynamic API mapping

### Dual-Transport Consistency

**Validation Approach**: Same business logic tested via both HTTP and WebSocket to ensure protocol consistency:

```csharp
// HTTP client testing
var httpResponse = await httpClient.CreateAccountAsync(request);

// WebSocket client testing
var wsResponse = await webSocketClient.CreateAccountAsync(request);

// Response consistency validation
Assert.Equal(httpResponse.Data, wsResponse.Data);
```

### CI/CD Integration Details

**Environment Variables**:
- `DAEMON_MODE=true` - Enables non-interactive CI testing
- Build configuration: `--configuration Release`
- Test execution: `--no-build` for performance

**Exit Codes**:
- `0` - All tests passed
- `Non-zero` - Test failures or errors

**Makefile Integration**: All CI commands available via Makefile for local reproduction:

```bash
make test-ci                   # Complete pipeline
make test-unit                 # Unit tests only
make test-http-daemon          # HTTP integration (CI mode)
make test-edge-daemon          # WebSocket testing (CI mode)
make test-infrastructure       # Infrastructure validation
```

## Production Deployment Testing

### NuGet SDK Publishing

**Automated Publishing Pipeline**: Master branch pushes trigger:
- Semantic version calculation and tagging
- Client SDK package creation and NuGet publishing
- Multi-version compatibility testing
- Production environment protection

**Compatibility Testing**:
- **Backwards Compatibility**: `edge-tester` using published NuGet SDK
- **Forward Compatibility**: `edge-tester` using current generated SDK
- **Breaking Change Detection**: Automated validation prevents API breaks

### Environment-Specific Testing

**Local Development**:
```bash
make test                      # All tests
make test PLUGIN=service       # Service-specific testing
```

**CI/CD Environment**:
```bash
DAEMON_MODE=true make test-ci  # Non-interactive pipeline
```

**Production Validation**:
- Infrastructure health checks
- Service discovery validation
- Protocol compatibility verification
- Performance benchmarking

## Testing Best Practices

### Service Development Workflow

1. **Schema First**: Define OpenAPI specification in `/schemas/`
2. **Generate**: Create service structure with `make generate-services PLUGIN=service`
3. **Implement**: Write business logic in service implementation class
4. **Test Locally**: Use `make test PLUGIN=service` for focused testing
5. **Integration**: Validate with `make test-http` and `make test-edge`
6. **CI Validation**: Ensure `make test-ci` passes before merge

### Test Organization

**Unit Tests**: Focus on individual service logic and configuration
**Integration Tests**: Validate service-to-service communication
**Protocol Tests**: Ensure WebSocket and HTTP consistency
**Infrastructure Tests**: Verify deployment and connectivity

### Performance Considerations

**Test Execution Time**:
- Unit tests: ~10-15 seconds total
- HTTP integration: ~30-45 seconds
- WebSocket protocol: ~45-60 seconds
- Complete CI pipeline: ~5-8 minutes

**Parallel Execution**: Tests run in parallel where possible for performance
**Resource Optimization**: Docker containers cleaned up automatically after testing

## Troubleshooting

### Common Testing Issues

**Test Project Not Found**:
```bash
# Ensure test project exists
make generate-services PLUGIN=service
make test PLUGIN=service
```

**Build Errors After Generation**:
```bash
# Clean and regenerate
make clean PLUGIN=service
make generate-services PLUGIN=service
make build
```

**CI Pipeline Failures**:
```bash
# Reproduce locally
make test-ci

# Check individual steps
make generate-services-for-consistency
make test-unit
make test-infrastructure
```

### Development Guidelines

1. **Always test before commit**: Use `make test-ci` to reproduce CI environment
2. **Service-specific development**: Use `PLUGIN=service` parameters for focused work
3. **Schema validation**: Ensure OpenAPI schemas are valid before generation
4. **Protocol consistency**: Validate both HTTP and WebSocket behavior
5. **Documentation updates**: Keep testing docs current with pipeline changes

This comprehensive testing architecture ensures Bannou services maintain perfect consistency across development, testing, and production environments while providing developer-friendly workflows for efficient service development.
