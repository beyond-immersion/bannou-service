# Bannou Testing Documentation

Comprehensive testing documentation for Bannou's schema-driven microservices architecture with WebSocket-first edge gateway and CI/CD integration.

## 🚨 CRITICAL: Test Architecture Boundaries (READ FIRST)

**BEFORE writing ANY test, you MUST understand these isolation rules**:

### Plugin Isolation (MANDATORY)
- **`unit-tests/`**: Can ONLY reference `bannou-service`. CANNOT reference ANY `lib-*` plugins.
- **`lib-*.tests/`**: Can ONLY reference their own `lib-*` plugin + `bannou-service`. CANNOT reference other `lib-*` plugins.
- **`lib-testing/`**: Can ONLY reference `bannou-service`. CANNOT reference ANY other `lib-*` plugins.
- **`tools/http-tester/`**: Can reference all services via generated clients.
- **`tools/edge-tester/`**: Can test all services via WebSocket protocol.

### Critical Examples
- ❌ **WRONG**: Testing `AuthServiceConfiguration` in `lib-testing` (cannot reference `lib-auth`)
- ✅ **CORRECT**: Testing `AuthServiceConfiguration` in `lib-auth.tests` (own plugin)
- ❌ **WRONG**: Testing `AuthServiceConfiguration` in `unit-tests` (cannot reference plugins)
- ✅ **CORRECT**: Testing auth endpoints in `tools/http-tester` (service integration)

### Quick Decision Guide
- **Testing specific service logic?** → `plugins/lib-{service}.tests/`
- **Testing service-to-service calls?** → `tools/http-tester/`
- **Testing WebSocket protocol?** → `tools/edge-tester/`
- **Testing core framework?** → `unit-tests/`
- **Testing infrastructure health?** → `lib-testing/`

## Overview

Bannou implements a **progressive CI/CD pipeline** with comprehensive testing coverage including:

- **Schema-Driven Development**: OpenAPI specifications drive automatic test generation
- **Service-Specific Testing**: Individual service plugin testing with `make test PLUGIN=service`
- **Dual-Transport Validation**: HTTP and WebSocket protocol consistency testing
- **GitHub Actions Integration**: Complete progressive automated pipeline
- **189+ Automated Tests**: Unit tests, integration tests, and protocol validation

## Quick Start

```bash
# Test everything
make test                      # All 189+ tests across all services

# Test specific services
make test PLUGIN=account       # Test account service only
make test PLUGIN=auth          # Test auth service only
make test PLUGIN=connect       # Test connect service only

# Complete CI pipeline locally
make test-ci                   # Matches GitHub Actions progressive pipeline

# Individual test types
make test-unit                 # Unit tests only
make test-http                 # HTTP endpoint testing
make test-edge                 # WebSocket protocol testing
make test-infrastructure       # Infrastructure validation
```

## Testing Architecture

**CRITICAL: Test Isolation Boundaries and Dependencies**

Before understanding the specific test types, you must understand the strict isolation boundaries that prevent cross-contamination and architectural violations:

### Test Isolation Rules (MANDATORY)

#### 1. Plugin Isolation Principle
**Rule**: Each service plugin is completely isolated and cannot reference other service plugins.

**What this means**:
- `lib-auth.tests` CANNOT reference `lib-account` or any other `lib-*` plugin
- `lib-testing` CANNOT reference `lib-auth`, `lib-account`, or any other `lib-*` plugin
- `unit-tests` CANNOT reference ANY `lib-*` plugin (they are not loaded in unit test context)

**Why**: Plugins are dynamically loaded at runtime. In test contexts, only specific plugins are loaded, so cross-plugin references will fail.

#### 2. Reference Hierarchy
**What each test type CAN reference**:

- **`unit-tests/`**: Only `bannou-service` project (core framework code)
- **`lib-*.tests/`**: Only their own `lib-*` plugin + `bannou-service` project + mocks
- **`lib-testing/`**: Only `bannou-service` project (cannot reference other plugins)
- **`tools/http-tester/`**: All service plugins via NSwag-generated clients + `bannou-service`
- **`tools/edge-tester/`**: All service plugins via WebSocket protocol + `bannou-service`

#### 3. Configuration Testing Rules
**Problem**: You want to test `AuthServiceConfiguration` but it lives in `lib-auth` plugin.

**Solutions by test type**:
- **Unit test in `unit-tests/`**: ❌ CANNOT - unit-tests cannot reference `lib-auth` plugin
- **Unit test in `lib-auth.tests/`**: ✅ CAN - tests its own plugin with mocks
- **Infrastructure test in `lib-testing/`**: ❌ CANNOT - lib-testing cannot reference `lib-auth` plugin
- **HTTP integration test**: ✅ CAN - tools/http-tester can call auth service endpoints
- **WebSocket integration test**: ✅ CAN - tools/edge-tester can test auth via WebSocket protocol

**To test configuration binding mechanism itself**:
- Create test configuration class IN `lib-testing` that mimics the pattern
- Test the core `IServiceConfiguration.BuildConfiguration<T>()` mechanism
- Do NOT try to test the actual `AuthServiceConfiguration` from infrastructure tests

#### 4. When to Use Each Test Type

**Use `unit-tests/`** when:
- Testing core framework functionality (configuration binding mechanism, plugin loading, etc.)
- Testing shared utilities in `bannou-service` project
- Cannot and should not reference any `lib-*` plugins

**Use `lib-*.tests/`** when:
- Testing specific service logic with mocked dependencies
- Testing service configuration of that specific service only
- Testing service-specific business logic in isolation

**Use `lib-testing/`** when:
- Testing infrastructure components (Docker, Redis, RabbitMQ, basic connectivity)
- Testing core framework functionality with minimal service load
- Validating that the basic service loading mechanism works
- NEVER for testing specific service functionality

**Use `tools/http-tester/`** when:
- Testing service-to-service communication
- Testing actual API endpoints and business logic
- Testing authentication flows and service integration
- Testing request/response models and validation

**Use `tools/edge-tester/`** when:
- Testing WebSocket protocol implementation
- Testing Connect service routing and binary protocol
- Testing real-time features and client-server communication

### 1. Unit Tests (3,100+ Total)

**Service Test Structure**: Each service has comprehensive unit tests in `plugins/plugins/lib-{service}.tests/`:

```
plugins/lib-account.tests/           # Account service tests
plugins/lib-auth.tests/              # Authentication service tests
plugins/lib-behavior.tests/          # Behavior service tests
plugins/lib-connect.tests/           # Connect service tests
plugins/lib-game-session.tests/      # Game session tests
plugins/lib-permission.tests/        # Permission service tests
plugins/lib-website.tests/           # Website service tests
bannou-service.tests/                # Core framework tests
```

**Generated Test Structure**: Tests follow xUnit patterns with proper dependency injection:

```csharp
public class AccountServiceTests
{
    private readonly Mock<ILogger<AccountService>> _mockLogger = new();
    private readonly Mock<AccountServiceConfiguration> _mockConfiguration = new();

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        var service = new AccountService(_mockLogger.Object, _mockConfiguration.Object);
        Assert.NotNull(service);
    }
}
```

### 2. HTTP Integration Testing (`tools/http-tester`)

**Purpose**: Direct HTTP endpoint validation for service logic testing
**Location**: `tools/http-tester/` project

**Features**:
- Direct service-to-service communication using generated clients
- JWT authentication workflows
- Comprehensive API endpoint validation
- Request/response model validation
- Service integration testing

**Usage**:
```bash
# Interactive mode
dotnet run --project tools/http-tester

# CI/CD daemon mode
DAEMON_MODE=true dotnet run --project tools/http-tester --configuration Release
```

### 3. WebSocket Protocol Testing (`tools/edge-tester`)

**Purpose**: Complete Connect service edge gateway and binary protocol validation
**Location**: `tools/edge-tester/` project

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
dotnet run --project tools/edge-tester

# CI/CD daemon mode
DAEMON_MODE=true dotnet run --project tools/edge-tester --configuration Release
```

#### Edge Test Development Guidelines

**Typed Proxy Requirement (MANDATORY)**

Edge tests MUST use **typed service proxies** (e.g., `adminClient.Realm.CreateRealmAsync()`) instead of raw `InvokeAsync` calls. Typed proxies provide:

1. **Compile-time validation** of request/response types against the schema
2. **Correct endpoint paths** (prevents typos like `/game-service/create` vs `/game-service/services/create`)
3. **Correct property names** (prevents mismatches like `name` vs `displayName`)
4. **Correct enum values** (prevents case issues like `CHARACTER` vs `character`)
5. **SDK proxy testing** -- validates that the generated typed proxies themselves work correctly over the binary protocol

New edge tests should extend `BaseWebSocketTestHandler` which provides:
- `RunWebSocketTest()` for standardized test execution with error handling
- `FormatError()` for consistent error formatting
- `GenerateUniqueCode()` for test data uniqueness
- Typed resource creation helpers (`CreateTestRealmAsync`, `CreateTestSpeciesAsync`, etc.)

**Example -- correct pattern:**
```csharp
var response = await adminClient.Character.CreateCharacterAsync(new CreateCharacterRequest
{
    Name = "Test Character",
    RealmId = realmId,
    SpeciesId = speciesId
}, timeout: TimeSpan.FromSeconds(5));

if (!response.IsSuccess || response.Result == null)
{
    Console.WriteLine($"   Failed: {FormatError(response.Error)}");
    return false;
}

return response.Result.CharacterId != Guid.Empty;
```

**Example -- incorrect pattern (do not use for service API tests):**
```csharp
// WRONG: Raw InvokeAsync bypasses typed proxy validation
var response = await adminClient.InvokeAsync<object, JsonElement>(
    "POST", "/character/create", new { name = "Test", realmId = id }, ...);
```

**Legitimate InvokeAsync Exceptions**

Raw `InvokeAsync` is permitted ONLY in these specific categories. If adding a new exception, document WHY typed proxies cannot be used:

| Category | Files | Rationale |
|----------|-------|-----------|
| **Raw protocol testing** | `ConnectWebSocketTestHandler.cs` | Tests binary header construction, frame parsing, malformed messages -- operates below the proxy abstraction layer |
| **SDK parity testing** | `TypeScriptParityTestHandler.cs` | Must use raw JSON to compare byte-level output with TypeScript SDK |
| **Infrastructure/routing testing** | `SplitServiceRoutingTestHandler.cs` | Tests orchestrator deployment and dynamic routing topology changes |
| **Protocol mechanism testing** | `CapabilityFlowTestHandler.cs`, `ClientEventTestHandler.cs` | Tests capability push and event delivery mechanics, not service APIs |
| **Dynamic endpoint testing** | `GameSessionWebSocketTestHandler.cs` | Tests runtime-created shortcuts with dynamic GUIDs that have no typed proxy |

### 4. Infrastructure Testing (`lib-testing`)

**Purpose**: Docker Compose service availability and minimal infrastructure validation
**Service Scope**: TESTING service ONLY (uses `BANNOU_SERVICES: "testing"`)
**Components**: OpenResty, Redis, RabbitMQ, MySQL basic connectivity

**CRITICAL ARCHITECTURAL CONSTRAINTS**:
- **lib-testing CANNOT reference any other service plugins**
- **Uses minimal configuration with only TESTING service loaded**
- **Environment**: `.env.ci.infrastructure` with `TESTING_SERVICE_ENABLED=true` only
- **Docker Compose**: `docker-compose.ci.infrastructure.yml` builds with `BANNOU_SERVICES: "testing"`

**What Infrastructure Tests CAN Test**:
- Basic Docker Compose service health (databases, message queues)
- Core framework configuration binding mechanism (using test-specific config classes)
- Service plugin loading infrastructure (TestingService only)
- Redis and RabbitMQ connectivity and health

**What Infrastructure Tests CANNOT Test**:
- Real service configurations from other plugins (AuthServiceConfiguration, etc.)
- Service-to-service communication (use tools/http-tester for this)
- Business logic from specific services (use lib-*.tests for this)
- Cross-plugin functionality (plugins are isolated)

**Usage**:
```bash
# Standalone infrastructure testing
make test-infrastructure

# Full infrastructure with Docker Compose
make build-compose
make ci-up-compose
make test-infrastructure
```

**Testing Configuration Binding Example**:
If you want to test that environment variable binding works, create a configuration class IN lib-testing:

```csharp
// IN lib-testing/TestingService.cs - NOT referencing other plugins
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class TestConfigurationBinding
{
    public string? TestSecret { get; set; }
    public string? TestValue { get; set; }
}

// Test the core mechanism with THIS class, not AuthServiceConfiguration
```

## GitHub Actions Progressive CI/CD Pipeline

### Pipeline Overview

The comprehensive CI/CD pipeline (`.github/workflows/ci.integration.yml`) ensures complete validation through progressive testing phases:

#### Generation and Build Phase
- **🔧 Generate Services (Initial)** - Create controllers/models from OpenAPI schemas
- **📦 Generate Client SDK (Initial)** - Build client SDK from service definitions
- **🏗️ Build All Projects** - Compile with Release configuration

#### Consistency Validation Phase
- **🔍 Generate Services (Conflict Detection)** - Re-generate to detect conflicts
- **🔄 Generate Client SDK (Consistency Check)** - Ensure SDK consistency

#### Infrastructure Testing Phase
- **🧪 Unit Tests** - 189+ tests across all service plugins
- **🏗️ Infrastructure Tests (Minimal)** - TESTING service only for reduced dependencies
- **🛑 Container Restart** - Clean separation between infrastructure and integration testing

#### Integration Testing Phase
- **🚀 Service Startup (Full)** - All services enabled for comprehensive integration testing
- **🔗 HTTP Integration Tests** - Service-to-service communication validation
- **📡 WebSocket Backwards Compatibility** - Published NuGet SDK compatibility
- **🚀 WebSocket Forward Compatibility** - Current generated SDK compatibility

### Local Pipeline Reproduction

```bash
# Complete progressive pipeline locally (matches GitHub Actions exactly)
make test-ci

# Individual pipeline components
make generate-services-for-consistency  # Consistency validation
make test-unit                          # Unit testing
make test-infrastructure                # Infrastructure testing (minimal deps)
make test-http-daemon                   # HTTP integration testing
make test-edge-daemon                   # WebSocket protocol testing
```

### Service-Specific Development Workflow

```bash
# Develop and test specific services
make clean PLUGIN=account              # Clean account service only
make generate-services PLUGIN=account  # Generate account service only
make test PLUGIN=account               # Test account service only

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
1. Define API in `schemas/account-api.yaml`
2. Generate service with `make generate-services PLUGIN=account`
3. Implement business logic in `AccountService.cs`
4. Test with `make test PLUGIN=account`
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
plugins/lib-{service}.tests/
├── GlobalUsings.cs              # xUnit global imports
├── {Service}ServiceTests.cs     # Main service tests
└── lib-{service}.tests.csproj   # Test project file
```

## Advanced Testing Features

### External Resource Testing Patterns

Bannou uses two patterns for testing code that depends on external resources (Lua scripts, YAML fixtures). Both patterns avoid embedding multi-line content in C# string literals, which prevents formatting issues and improves maintainability.

#### Pattern 1: Embedded Resources (Lua Scripts)

**Use case**: Production code that needs bundled resources (Lua scripts, SQL, templates)

**Example**: Redis Lua scripts in `lib-state`

**Directory structure**:
```
plugins/lib-state/
├── Scripts/
│   ├── TryCreate.lua         # Lua script files
│   └── TryUpdate.lua
├── Services/
│   └── RedisLuaScripts.cs    # Loader class
└── lib-state.csproj          # Embeds Scripts/*.lua
```

**Step 1: Create resource files**
```lua
-- Scripts/TryCreate.lua
-- KEYS[1] = fullKey, KEYS[2] = metaKey
-- ARGV[1] = JSON value, ARGV[2] = timestamp
-- Returns: 1 = success, -1 = already exists

if redis.call('EXISTS', KEYS[1]) == 1 then
    return -1
end
redis.call('JSON.SET', KEYS[1], '$', ARGV[1])
redis.call('HSET', KEYS[2], 'version', 1, 'created', ARGV[2], 'updated', ARGV[2])
return 1
```

**Step 2: Embed in .csproj**
```xml
<!-- lib-state.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Scripts\*.lua" />
</ItemGroup>
```

**Step 3: Create loader class**
```csharp
// Services/RedisLuaScripts.cs
public static class RedisLuaScripts
{
    private static readonly ConcurrentDictionary<string, string> _scriptCache = new();
    private static readonly Assembly _assembly = typeof(RedisLuaScripts).Assembly;

    public static string TryCreate => GetScript("TryCreate");
    public static string TryUpdate => GetScript("TryUpdate");

    public static string GetScript(string scriptName)
    {
        return _scriptCache.GetOrAdd(scriptName, name =>
        {
            var resourceName = $"BeyondImmersion.BannouService.State.Scripts.{name}.lua";
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Script '{name}' not found");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    public static IEnumerable<string> ListAvailableScripts()
    {
        const string prefix = "BeyondImmersion.BannouService.State.Scripts.";
        return _assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".lua"))
            .Select(n => n[prefix.Length..^4]);
    }
}
```

**Step 4: Write tests** (`lib-state.tests/RedisLuaScriptsTests.cs`)
```csharp
public class RedisLuaScriptsTests
{
    [Fact]
    public void TryCreate_LoadsFromEmbeddedResource()
    {
        var script = RedisLuaScripts.TryCreate;
        Assert.NotNull(script);
        Assert.Contains("JSON.SET", script);
        Assert.Contains("EXISTS", script);
    }

    [Fact]
    public void TryCreate_HasCorrectKeyStructure()
    {
        var script = RedisLuaScripts.TryCreate;
        Assert.Contains("KEYS[1]", script);  // fullKey
        Assert.Contains("KEYS[2]", script);  // metaKey
    }

    [Fact]
    public void GetScript_CachesScripts()
    {
        var script1 = RedisLuaScripts.GetScript("TryCreate");
        var script2 = RedisLuaScripts.GetScript("TryCreate");
        Assert.Same(script1, script2);  // Same cached instance
    }

    [Fact]
    public void ListAvailableScripts_ReturnsAllScripts()
    {
        var scripts = RedisLuaScripts.ListAvailableScripts().ToList();
        Assert.Contains("TryCreate", scripts);
        Assert.Contains("TryUpdate", scripts);
    }
}
```

#### Pattern 2: External Fixtures (YAML/ABML)

**Use case**: Test-only data files that would be corrupted by `dotnet format`

**Why external files?**: YAML indentation is significant. Embedding YAML in C# string literals causes:
- `dotnet format` to corrupt indentation
- Difficult-to-read test code
- Merge conflicts on formatting changes

**Example**: ABML behavior document fixtures in `lib-behavior.tests`

**Directory structure**:
```
plugins/lib-behavior.tests/
├── Compiler/
│   ├── fixtures/
│   │   ├── compiler_conditional.yml
│   │   └── compiler_comparison.yml
│   ├── TestFixtures.cs           # Fixture loader
│   └── BehaviorCompilerTests.cs  # Tests using fixtures
├── Cognition/
│   ├── fixtures/
│   │   └── template_valid.yml
│   └── CognitionTestFixtures.cs
└── lib-behavior.tests.csproj     # Copies fixtures to output
```

**Step 1: Create fixture files**
```yaml
# Compiler/fixtures/compiler_conditional.yml
version: "2.0"
metadata:
  id: test

context:
  variables:
    x: { type: float, default: 10 }

flows:
  main:
    actions:
      - cond:
          - when: "${x > 5}"
            then:
              - log: { message: "Greater" }
          - otherwise:
              - log: { message: "Less" }
```

**Step 2: Configure .csproj to copy fixtures**
```xml
<!-- lib-behavior.tests.csproj -->
<ItemGroup>
  <!-- Copy YAML test fixtures to output directory -->
  <None Include="Compiler\fixtures\**\*.yml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="Cognition\fixtures\**\*.yml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**Step 3: Create fixture loader**
```csharp
// Compiler/TestFixtures.cs
public static class TestFixtures
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "Compiler", "fixtures");

    public static string Load(string name)
    {
        var path = Path.Combine(FixturesPath, $"{name}.yml");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}");
        return File.ReadAllText(path);
    }

    public static bool Exists(string name)
    {
        return File.Exists(Path.Combine(FixturesPath, $"{name}.yml"));
    }
}
```

**Step 4: Use in tests**
```csharp
public class BehaviorCompilerTests
{
    [Fact]
    public void Compile_ConditionalFlow_GeneratesCorrectBytecode()
    {
        // Load fixture - not corrupted by dotnet format
        var yaml = TestFixtures.Load("compiler_conditional");

        var result = _compiler.Compile(yaml);

        Assert.True(result.Success);
        Assert.NotNull(result.Bytecode);
    }
}
```

#### When to Use Each Pattern

| Pattern | Use When | Benefits |
|---------|----------|----------|
| **Embedded Resources** | Production code needs bundled files | Single assembly deployment, runtime access |
| **External Fixtures** | Test-only data files | Format-safe, easy to edit, version-controlled |

**Common mistakes to avoid**:
- ❌ Embedding YAML in C# verbatim strings (breaks on `dotnet format`)
- ❌ Using `EmbeddedResource` for test-only files (harder to update)
- ❌ Hardcoding fixture paths (use `AppContext.BaseDirectory`)
- ❌ Forgetting `CopyToOutputDirectory` in .csproj (fixtures not found at runtime)

### WebSocket Binary Protocol

**Protocol Specification**: 31-byte binary header + JSON payload
- **Message Flags**: 1 byte - routing control
- **Channel**: 2 bytes - service channel identification
- **Sequence**: 4 bytes - message ordering
- **Service GUID**: 16 bytes - client-specific service routing
- **Message ID**: 8 bytes - request/response correlation

**Testing Implementation**: `tools/edge-tester` validates complete protocol compliance including:
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
- **Backwards Compatibility**: `tools/edge-tester` using published NuGet SDK
- **Forward Compatibility**: `tools/edge-tester` using current generated SDK
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

### Critical Test Architecture Violations (MUST AVOID)

#### ❌ WRONG: Trying to Reference Other Plugins from Infrastructure Tests
```csharp
// In lib-testing/TestingService.cs - THIS IS WRONG
public Task<TestResult> TestAuthConfiguration()
{
    // ❌ ERROR: lib-testing cannot reference AuthServiceConfiguration
    var authConfig = GetService<AuthServiceConfiguration>();
    // This will fail because lib-testing cannot reference lib-auth
}
```

#### ✅ CORRECT: Test Configuration Mechanism in Infrastructure Tests
```csharp
// In lib-testing/TestingService.cs - THIS IS CORRECT
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class TestConfigForBindingValidation
{
    public string? TestSecret { get; set; }
}

public Task<TestResult> TestConfigurationBinding()
{
    // ✅ CORRECT: Test the core binding mechanism with lib-testing's own config
    var config = IServiceConfiguration.BuildConfiguration<TestConfigForBindingValidation>();
    // This tests that BANNOU_ prefix binding works without referencing other plugins
}
```

#### ❌ WRONG: Putting Service-Specific Tests in Wrong Place
```csharp
// In unit-tests/AuthServiceTests.cs - THIS IS WRONG
public void TestAuthServiceConfiguration()
{
    // ❌ ERROR: unit-tests cannot reference AuthServiceConfiguration
    var service = new AuthService(...); // This will fail
}
```

#### ✅ CORRECT: Service-Specific Tests in Correct Location
```csharp
// In lib-auth.tests/AuthServiceTests.cs - THIS IS CORRECT
public void TestAuthServiceConfiguration()
{
    // ✅ CORRECT: lib-auth.tests can test its own plugin
    var service = new AuthService(...); // This works
}
```

#### ❌ WRONG: Testing Real Service Integration from Infrastructure Tests
```csharp
// In lib-testing/ - THIS IS WRONG
public async Task TestRealAuthEndpoint()
{
    // ❌ ERROR: lib-testing only has TESTING service, not AUTH service
    var authClient = GetService<IAuthClient>(); // This will fail
}
```

#### ✅ CORRECT: Service Integration Testing in HTTP Tester
```csharp
// In tools/http-tester/Tests/AuthTestHandler.cs - THIS IS CORRECT
public async Task TestRealAuthEndpoint()
{
    // ✅ CORRECT: tools/http-tester has all services and can test real integration
    var authClient = GetServiceClient<IAuthClient>(); // This works
}
```

### Architecture Decision Rules

**When you want to test something, ask these questions**:

1. **Am I testing a specific service's functionality?** → Use `plugins/lib-{service}.tests/`
2. **Am I testing service-to-service communication?** → Use `tools/http-tester/`
3. **Am I testing WebSocket protocol or Connect service?** → Use `tools/edge-tester/`
4. **Am I testing core framework functionality?** → Use `unit-tests/`
5. **Am I testing basic infrastructure connectivity?** → Use `lib-testing/`

**RED FLAGS that indicate wrong test placement**:
- Trying to reference `lib-auth` from `lib-testing` ❌
- Trying to reference ANY `lib-*` from `unit-tests` ❌
- Trying to test business logic in `lib-testing` ❌
- Trying to test infrastructure in `lib-*.tests` ❌

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
