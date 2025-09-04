# Bannou Service Testing Architecture

This document describes the dual testing approach implemented for Bannou services.

## Overview

The testing infrastructure provides two complementary testing clients that share the same test definitions:

- **HTTP Tester** (`http-tester/`) - Tests services directly via HTTP endpoints
- **Edge Tester** (`edge-tester/`) - Tests services via WebSocket/binary protocol (edge layer)
- **Shared Testing Core** (`lib-testing-core/`) - Common test definitions and interfaces

## Why Two Testing Approaches?

When debugging service issues, it's critical to isolate whether problems are:

1. **Service Logic Issues** - HTTP Tester fails, Edge Tester fails
2. **Edge/Protocol Issues** - HTTP Tester passes, Edge Tester fails  
3. **Infrastructure Issues** - Both tests fail consistently

This follows industry best practices for testing distributed systems where you can isolate protocol/transport layer issues from core business logic.

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

## Edge Tester

**Purpose**: WebSocket/binary protocol testing  
**Use Case**: Validate edge gateway, protocol serialization, WebSocket connections

The Edge Tester retains the original WebSocket/binary protocol implementation from the service-tester, testing the complete client experience including:

- WebSocket connection establishment
- Binary message protocol (flags, compression, encryption)
- Authentication over WebSocket
- Request/response correlation

### Running

```bash
cd edge-tester
dotnet run
```

## Shared Testing Core

**Location**: `lib-testing-core/`

### Key Components

- `ITestClient` - Abstraction for HTTP vs WebSocket clients
- `ServiceTest` - Test definition that works with either client
- `IServiceTestHandler` - Groups related tests
- `TestResult` / `TestResponse<T>` - Standardized result types
- `TestConfiguration` - Configuration for both clients

### Creating New Tests

1. Implement `IServiceTestHandler`:

```csharp
public class MyServiceTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestMyEndpoint, "MyEndpoint", "MyService", "Description")
        };
    }

    private static async Task<TestResult> TestMyEndpoint(ITestClient client, string[] args)
    {
        var response = await client.PostAsync<MyResponse>("api/myservice/endpoint", requestBody);
        
        if (!response.Success)
            return TestResult.Failed($"Request failed: {response.ErrorMessage}");
            
        return TestResult.Successful("Test completed successfully");
    }
}
```

2. Add handler to both test clients' `LoadServiceTests()` methods.

## Development Workflow

1. **Write tests first** - Define expected API behavior
2. **Run HTTP Tester** - Validate core service implementation  
3. **Run Edge Tester** - Validate end-to-end client experience
4. **Compare results** - Isolate issues to service vs. edge layers

## Configuration Options

Both testers support configuration via:
- `Config.json` files
- Environment variables  
- Command line arguments

Environment variable format: `HTTP_BASE_URL`, `CLIENT_USERNAME`, etc.

## Integration with CI/CD

Both testers can be run in automated pipelines:

```bash
# Test core services
dotnet run --project http-tester -- --client-username=ciuser --client-password=cipass

# Test edge layer  
dotnet run --project edge-tester -- --client-username=ciuser --client-password=cipass
```

Exit codes indicate success (0) or failure (non-zero).