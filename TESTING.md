# Bannou Service Testing Architecture

This document describes the comprehensive schema-driven dual testing approach implemented for Bannou services.

## Overview

The testing infrastructure provides automated test generation from OpenAPI schemas with dual-transport validation:

- **Schema-Driven Test Generation** - Automatic test creation from OpenAPI YAML specifications
- **HTTP Tester** (`http-tester/`) - Tests services directly via HTTP endpoints
- **WebSocket Tester** (via `lib-testing-core/WebSocketTestClient`) - Tests via Connect service binary protocol
- **Shared Testing Core** (`lib-testing-core/`) - Common test definitions, schema parsing, and dual-transport framework
- **Transport Consistency Validation** - Ensures HTTP and WebSocket produce identical results

## Why Schema-Driven Dual Testing?

This comprehensive testing approach provides multiple layers of validation:

1. **Schema Compliance** - All requests/responses validated against OpenAPI contracts
2. **Service Logic Issues** - HTTP Tester fails, WebSocket Tester fails  
3. **Protocol/Transport Issues** - HTTP Tester passes, WebSocket Tester fails
4. **Transport Consistency** - Detects discrepancies between HTTP and WebSocket behavior
5. **Comprehensive Coverage** - Automatic generation of success, validation, and authorization tests
6. **Regression Prevention** - Schema changes automatically generate new test cases

This follows industry best practices for testing distributed systems with the added benefit of automated test generation from API contracts.

## HTTP Tester

**Purpose**: Direct HTTP endpoint testing  
**Use Case**: Validate core service logic, API contracts, database operations  

### Configuration

Create `http-tester/Config.json`:
```json
{
  "Http_Base_Url": "http://localhost:80",
  "Register_Endpoint": "api/accounts/create",
  "Login_Credentials_Endpoint": "api/auth/login", 
  "Client_Username": "testuser",
  "Client_Password": "TestPassword123!"
}
```

### Running

```bash
cd http-tester
dotnet run
```

### Features

- Direct HTTP calls to Bannou service endpoints
- JWT token authentication
- JSON request/response handling
- Interactive test menu
- Detailed success/failure reporting

## WebSocket Protocol Testing

**Purpose**: WebSocket/binary protocol testing via Connect service edge gateway
**Use Case**: Validate Connect service routing, protocol serialization, WebSocket connections

The WebSocket testing client implements the complete Bannou binary protocol:

- **Service Discovery**: Client receives method → GUID mappings at connection
- **Binary Protocol**: 24-byte headers (16-byte service GUID + 8-byte message ID) + payload
- **Zero-Copy Routing**: Connect service routes via GUID without payload inspection
- **Authentication Workflows**: WebSocket-based login and token refresh
- **Concurrent Requests**: Correlation ID management for multiple simultaneous requests
- **Transport Validation**: Ensures WebSocket responses match HTTP responses exactly

### Running

```bash
cd edge-tester
dotnet run
```

## Shared Testing Core

**Location**: `lib-testing-core/`

### Key Components

- `SchemaTestGenerator` - Generates tests automatically from OpenAPI YAML schemas
- `ITestClient` - Abstraction for HTTP vs WebSocket clients (now includes `IDisposable`)
- `WebSocketTestClient` - Production-ready WebSocket client implementing Bannou binary protocol
- `ISchemaTestHandler` - Enhanced test handlers supporting schema-driven generation
- `DualTransportTestRunner` - Executes same tests via both HTTP and WebSocket
- `ServiceTest` - Test definition that works with either client
- `TestResult` / `TestResponse<T>` - Standardized result types
- `TestConfiguration` - Configuration for both clients including WebSocket endpoint
- `TestGenerationDemo` - Interactive demonstration of schema-driven testing capabilities

### Schema-Driven Test Generation

1. **Automatic Test Generation** - Tests generated from OpenAPI schemas:

```csharp
public class EnhancedAccountTestHandler : ISchemaTestHandler
{
    public async Task<ServiceTest[]> GetSchemaBasedTests(SchemaTestGenerator generator)
    {
        var schemaPath = GetSchemaFilePath();
        return await generator.GenerateTestsFromSchema(schemaPath);
    }

    public string GetSchemaFilePath()
    {
        return Path.Combine("schemas", "accounts-api.yaml");
    }

    // Manual tests still supported
    public ServiceTest[] GetServiceTests()
    {
        return new[] { /* custom tests */ };
    }
}
```

2. **Generated Test Types**:
   - **Success Tests**: Valid requests with proper authentication
   - **Validation Tests**: Missing required fields, invalid types, format violations
   - **Authorization Tests**: Unauthorized access attempts
   - **Transport Consistency**: Same test via HTTP and WebSocket

3. **Dual Transport Execution**:

```csharp
var dualRunner = new DualTransportTestRunner(configuration);
var results = await dualRunner.RunDualTransportTests(handler);

// Analyze transport consistency
var discrepancies = results.Where(r => r.HasTransportDiscrepancy);
```

## Development Workflow

### Schema-First Testing Approach
1. **Define OpenAPI Schema** - Create comprehensive API contract in YAML
2. **Generate Tests Automatically** - Schema drives comprehensive test generation
3. **Run HTTP Testing** - Validate core service implementation against schema
4. **Run WebSocket Testing** - Validate Connect service routing and binary protocol
5. **Verify Transport Consistency** - Ensure HTTP and WebSocket produce identical results
6. **Analyze Discrepancies** - Investigate any transport-specific behaviors

### Manual Testing Workflow  
1. **Write custom tests** - For complex workflows not covered by schema generation
2. **Implement business logic** - Services inherit from generated abstract controllers
3. **Run comprehensive test suite** - 167+ tests covering all scenarios
4. **Validate results** - Both generated and manual tests pass

## Configuration Options

Both testers support configuration via:
- `Config.json` files
- Environment variables  
- Command line arguments

Environment variable format: `HTTP_BASE_URL`, `CLIENT_USERNAME`, etc.

## Integration with CI/CD

All testing approaches can be run in automated pipelines:

```bash
# Direct HTTP service testing
dotnet run --project http-tester -- --client-username=ciuser --client-password=cipass

# WebSocket protocol testing (via lib-testing-core)
dotnet run --project edge-tester -- --client-username=ciuser --client-password=cipass

# All unit tests (167 total across all services)
dotnet test

# Schema-driven test generation demo
# (Demonstrates automatic test generation from OpenAPI schemas)
var demo = TestGenerationDemo.RunDemo(configuration);
```

Exit codes indicate success (0) or failure (non-zero).

## Advanced Testing Features

### Schema-Driven Test Generation Details

**Automatic Test Coverage**:
- **Success Scenarios**: Valid requests with proper data types and authentication
- **Required Field Validation**: Tests for each required field missing from requests
- **Type Validation**: Invalid data types (string instead of number, etc.)  
- **Format Validation**: Email format, password complexity, pattern matching
- **Authorization Tests**: Endpoints requiring authentication tested without tokens

**Test Generation Process**:
1. Parse OpenAPI YAML schema using YamlDotNet
2. Extract endpoints, request/response models, and validation rules
3. Generate test data that satisfies/violates schema constraints
4. Create test methods for success and failure scenarios
5. Handle authentication dependencies and stateful workflows

### Dual-Transport Consistency Validation

**Transport Comparison**:
- Same test logic executed via HTTP and WebSocket clients
- Response data comparison with intelligent error pattern matching
- Transport discrepancy detection and reporting
- Performance comparison between transport methods

**Equivalent Failure Analysis**:
```csharp
private bool IsEquivalentFailure(string httpMessage, string wsMessage)
{
    var errorPatterns = new[]
    {
        "validation", "unauthorized", "forbidden", "not found", 
        "bad request", "missing", "invalid", "required", "timeout"
    };
    
    // Failures are equivalent if both contain the same error patterns
    return errorPatterns.Any(pattern => 
        httpMessage.Contains(pattern) && wsMessage.Contains(pattern));
}
```

### WebSocket Binary Protocol Implementation

**Protocol Features**:
- **Service Discovery**: Dynamic method → GUID mapping at connection time
- **Binary Message Format**: 24-byte header + JSON payload
- **Correlation IDs**: Request/response matching for concurrent operations
- **Authentication Flow**: WebSocket-based login with JWT tokens
- **Connection Management**: Automatic reconnection and heartbeat functionality

**Implementation Details**:
```csharp
// Binary message structure
var binaryMessage = new byte[24 + payload.Length];
Array.Copy(serviceGuidBytes, 0, binaryMessage, 0, 16);    // Service GUID
Array.Copy(messageIdBytes, 0, binaryMessage, 16, 8);      // Message ID  
Array.Copy(payload, 0, binaryMessage, 24, payload.Length); // JSON payload
```

### Test Reporting and Analysis

**Comprehensive Reporting**:
- Test execution results with transport comparison
- Schema compliance validation results  
- Performance metrics and timing analysis
- Transport discrepancy detection with detailed explanations
- JSON and Markdown report generation for CI/CD integration

**Demo System**:
The `TestGenerationDemo` class provides interactive demonstration of:
- Schema parsing and test generation capabilities
- Transport availability validation
- Dual-transport test execution with result analysis
- Comprehensive reporting in multiple formats

This testing architecture ensures that Bannou services maintain perfect API consistency across both HTTP (development) and WebSocket (production) transport layers while providing comprehensive automated test coverage derived directly from API contracts.
