# Client SDK Enhancement Plan

**Document Created**: 2025-12-02
**Status**: Planning / Analysis Complete
**Priority**: Pre-Stride Integration Requirement

## Executive Summary

The Bannou Client SDK (`Bannou.Client.SDK`) currently only supports **Service Mode** (HTTP-based Dapr service-to-service calls). To support game clients like Stride, we need to add **Client Mode** (WebSocket binary protocol connections). The Protocol classes already exist and are well-designed for extraction - they just need to be included in the SDK generation process.

## Critical CI/CD Finding

**The compatibility testing infrastructure exists but isn't fully utilized.**

### Current CI Pipeline (10 Steps)
1. Generate Services
2. Generate Client SDK
3. Build All Projects
4. Re-Generate (conflict detection)
5. Unit Tests
6. Infrastructure Tests
7. HTTP Integration Tests
8. **WebSocket Backward Compatibility Tests** ← Uses `UsePublishedSDK=true`
9. **WebSocket Forward Compatibility Tests** ← Uses `UseCurrentSDK=true`
10. Publish to NuGet (on master merge)

### The Gap

Edge-tester has MSBuild conditionals for SDK mode switching:
```xml
<!-- From edge-tester.csproj -->
<!-- Backwards Compatibility Testing: Use Published NuGet SDK -->
<ItemGroup Condition="'$(UsePublishedSDK)' == 'true'">
  <PackageReference Include="BeyondImmersion.Bannou.Client.SDK" Version="*" />
</ItemGroup>

<!-- Forward Compatibility Testing: Use Current Generated SDK -->
<ItemGroup Condition="'$(UseCurrentSDK)' == 'true'">
  <ProjectReference Include="../Bannou.Client.SDK/Bannou.Client.SDK.csproj" />
</ItemGroup>
```

**BUT** edge-tester also has direct project references that bypass the SDK:
```xml
<ProjectReference Include="../bannou-service/bannou-service.csproj" />
<ProjectReference Include="../lib-connect/lib-connect.csproj" />  <!-- Protocol classes! -->
```

This means edge-tester uses Protocol classes from `lib-connect` directly, NOT from the SDK. The backwards/forward compatibility tests are **not actually testing WebSocket protocol compatibility** - they only test model/client compatibility.

### Why This Matters

When the SDK properly includes Protocol classes AND edge-tester uses them FROM the SDK:

| Scenario | What Gets Tested |
|----------|------------------|
| **Backwards** (Published SDK + New Server) | Can old game clients talk to new server? |
| **Forward** (Current SDK + Current Server) | Does the new SDK work correctly? |

Without this, a breaking change to `BinaryMessage` or `MessageFlags` could ship without detection, breaking all existing game clients.

### The Architectural Problem

**Edge-tester is supposed to simulate an external game client**, but it currently references server-side code directly:

```xml
<!-- These should NOT be in edge-tester -->
<ProjectReference Include="../bannou-service/bannou-service.csproj" />
<ProjectReference Include="../lib-connect/lib-connect.csproj" />
<ProjectReference Include="..\lib-accounts\lib-accounts.csproj" />
<ProjectReference Include="..\lib-auth\lib-auth.csproj" />
<!-- ... all the lib-* projects ... -->
```

This defeats the entire purpose of edge testing. A real game client (Stride, Unity, etc.) would:
- Install the NuGet SDK package
- Use ONLY what's in that package
- Have NO access to server implementation details

Edge-tester currently has access to server internals, which means:
1. Tests might accidentally use server-side code paths
2. Tests don't validate the SDK is sufficient for real clients
3. Breaking changes to SDK might not be detected (server code still works)
4. The "client experience" isn't actually being tested

### The Fix

**Major rework required** - edge-tester must be refactored to ONLY depend on the Client SDK:

1. Add Protocol classes to SDK (Phase 1 of this document)
2. Create `BannouClient` high-level class in SDK (Phase 2)
3. **Remove ALL lib-* and bannou-service project references from edge-tester**
4. Edge-tester should ONLY have:
   ```xml
   <!-- Backwards Compatibility Testing: Use Published NuGet SDK -->
   <ItemGroup Condition="'$(UsePublishedSDK)' == 'true'">
     <PackageReference Include="BeyondImmersion.Bannou.Client.SDK" Version="*" />
   </ItemGroup>

   <!-- Forward Compatibility Testing: Use Current Generated SDK -->
   <ItemGroup Condition="'$(UseCurrentSDK)' == 'true'">
     <ProjectReference Include="../Bannou.Client.SDK/Bannou.Client.SDK.csproj" />
   </ItemGroup>
   ```
5. If edge-tester can't function with ONLY the SDK, that reveals SDK gaps that must be filled

This completes the compatibility testing loop AND validates the SDK is sufficient for real game clients.

## Current SDK Architecture

### What the SDK Currently Includes

**Generated via `scripts/generate-service.sh`** (MSBuild Target in `.csproj`):
```xml
<!-- From Bannou.Client.SDK.csproj -->
<Target Name="IncludeGeneratedFiles" BeforeTargets="BeforeBuild">
  <ItemGroup>
    <!-- Service Clients (HTTP/Dapr) -->
    <Compile Include="../lib-behavior/Generated/BehaviorClient.cs" />
    <Compile Include="../lib-permissions/Generated/PermissionsClient.cs" />
    <Compile Include="../lib-accounts/Generated/AccountsClient.cs" />
    <Compile Include="../lib-orchestrator/Generated/OrchestratorClient.cs" />
    <Compile Include="../lib-connect/Generated/ConnectClient.cs" />
    <Compile Include="../lib-auth/Generated/AuthClient.cs" />
    <Compile Include="../lib-game-session/Generated/GameSessionClient.cs" />
    <Compile Include="../lib-website/Generated/WebsiteClient.cs" />

    <!-- Model Classes -->
    <Compile Include="../lib-behavior/Generated/BehaviorModels.cs" />
    <!-- ... all other service models ... -->
  </ItemGroup>
</Target>

<!-- Shared Infrastructure -->
<ItemGroup>
  <Compile Include="../bannou-service/ServiceClients/IDaprClient.cs" />
  <Compile Include="../bannou-service/ServiceClients/DaprServiceClientBase.cs" />
  <Compile Include="../bannou-service/Services/IServiceAppMappingResolver.cs" />
  <Compile Include="../bannou-service/ApiException.cs" />
</ItemGroup>
```

### What's NOT in the SDK (But Should Be)

The WebSocket binary protocol classes in `lib-connect/Protocol/`:

| File | Purpose | Dependencies | SDK-Safe |
|------|---------|--------------|----------|
| `BinaryMessage.cs` | 31-byte header message parsing/serialization | `System.Text` only | Yes |
| `MessageFlags.cs` | Protocol flags enum (Binary, Encrypted, Response, etc.) | None | Yes |
| `NetworkByteOrder.cs` | Cross-platform big-endian byte utilities | `System.Buffers.Binary` | Yes |
| `GuidGenerator.cs` | Client-salted service GUID generation | `System.Security.Cryptography` | Yes |
| `ConnectionState.cs` | Client-side connection state management | None | Yes |
| `MessageRouter.cs` | Server-side message routing | **Server-only** | No |
| `BinaryProtocolTests.cs` | Unit tests | Test framework | No |

**Note**: `ConnectionState.cs` line 9 has comment: "Dependency-free class for Client SDK extraction" - this was clearly designed with SDK inclusion in mind.

## Two-Mode Architecture

### Service Mode (Currently Supported)

```
┌─────────────────┐     HTTP/Dapr      ┌─────────────────┐
│  .NET Service   │ ──────────────────▶│  Bannou Service │
│  (Game Server)  │    service mesh    │  (Auth, etc.)   │
└─────────────────┘                    └─────────────────┘

Uses: AuthClient, AccountsClient, etc. extending DaprServiceClientBase
Transport: HTTP via Dapr sidecar (automatic service discovery)
Auth: Service-to-service trust (internal network)
```

### Client Mode (NOT Currently Supported)

```
┌─────────────────┐    WebSocket       ┌─────────────────┐     Dapr      ┌─────────────────┐
│  Game Client    │ ──────────────────▶│ Connect Service │ ────────────▶│  Backend Svc    │
│  (Stride App)   │   binary protocol  │  (Edge Gateway) │              │  (Auth, etc.)   │
└─────────────────┘                    └─────────────────┘              └─────────────────┘

Uses: BinaryMessage, GuidGenerator, ConnectionState (NEW)
Transport: WebSocket with 31-byte binary header + JSON payload
Auth: JWT token obtained via HTTP login, then used in WebSocket handshake
```

## Edge-Tester Analysis

The edge-tester project demonstrates exactly what a game client would need to do. Currently, it does NOT use the SDK clients - instead implementing everything manually:

### What Edge-Tester Does (That SDK Should Provide)

**1. HTTP Authentication Flow** (`edge-tester/Program.cs:431-506`):
```csharp
// Manual HTTP client usage for login
using var httpClient = new HttpClient();
var loginRequest = new { Username = username, Password = password };
var response = await httpClient.PostAsJsonAsync($"{baseUrl}/auth/login", loginRequest);
var loginResult = await response.Content.ReadFromJsonAsync<LoginResponse>();
// Extract JWT token for WebSocket connection
```

**2. WebSocket Connection Setup** (`edge-tester/Program.cs:658-742`):
```csharp
// Manual WebSocket with JWT in query string
var wsUri = new Uri($"wss://server/ws?token={jwtToken}");
using var ws = new ClientWebSocket();
await ws.ConnectAsync(wsUri, cancellationToken);
```

**3. Binary Message Creation** (`edge-tester/Program.cs:673-678`):
```csharp
var binaryMessage = new BinaryMessage(
    flags: MessageFlags.None,
    channel: 0,
    sequenceNumber: sequenceNumber++,
    serviceGuid: serviceGuidFromCapabilities,
    messageId: GuidGenerator.GenerateMessageId(),
    payload: Encoding.UTF8.GetBytes(jsonPayload));

await ws.SendAsync(binaryMessage.ToByteArray(), WebSocketMessageType.Binary, true, ct);
```

**4. Response Parsing** (`edge-tester/Program.cs:709`):
```csharp
var result = await ws.ReceiveAsync(buffer, ct);
var receivedMessage = BinaryMessage.Parse(bufferArray, result.Count);
var jsonResponse = receivedMessage.GetJsonPayload();
```

**5. Service GUID Management** (from API discovery endpoint):
- Call `/connect/discover-apis` to get available services with client-salted GUIDs
- Cache GUIDs locally in `ConnectionState.ServiceMappings`
- Use cached GUIDs when constructing `BinaryMessage`

## Implementation Plan

### Phase 1: Include Protocol Classes in SDK Generation

**IMPORTANT**: The SDK `.csproj` file is **completely regenerated** by `scripts/generate-client-sdk.sh`. Any manual edits to the .csproj will be overwritten. Changes MUST be made to the generation script.

**File to Modify**: `scripts/generate-client-sdk.sh`

**Location in Script**: Around lines 85-97, where the static ItemGroup is added after the MSBuild target closes.

**Current Code (lines 89-97)**:
```bash
cat >> "$SDK_PROJECT" << 'EOF'
    </ItemGroup>
  </Target>

  <!-- Include only client-safe files from ServiceClients and Services -->
  <!-- Note: ServiceAppMappingResolver.cs is NOT included as it depends on server-side AppConstants -->
  <!-- Client SDK uses the fallback "bannou" app-id in DaprServiceClientBase -->
  <ItemGroup>
    <Compile Include="../bannou-service/ServiceClients/IDaprClient.cs" Condition="Exists('../bannou-service/ServiceClients/IDaprClient.cs')" />
    <Compile Include="../bannou-service/ServiceClients/DaprServiceClientBase.cs" Condition="Exists('../bannou-service/ServiceClients/DaprServiceClientBase.cs')" />
    <Compile Include="../bannou-service/Services/IServiceAppMappingResolver.cs" Condition="Exists('../bannou-service/Services/IServiceAppMappingResolver.cs')" />
    <Compile Include="../bannou-service/ApiException.cs" Condition="Exists('../bannou-service/ApiException.cs')" />
  </ItemGroup>

</Project>
EOF
```

**Modified Code (add Protocol files)**:
```bash
cat >> "$SDK_PROJECT" << 'EOF'
    </ItemGroup>
  </Target>

  <!-- Include only client-safe files from ServiceClients and Services -->
  <!-- Note: ServiceAppMappingResolver.cs is NOT included as it depends on server-side AppConstants -->
  <!-- Client SDK uses the fallback "bannou" app-id in DaprServiceClientBase -->
  <ItemGroup>
    <Compile Include="../bannou-service/ServiceClients/IDaprClient.cs" Condition="Exists('../bannou-service/ServiceClients/IDaprClient.cs')" />
    <Compile Include="../bannou-service/ServiceClients/DaprServiceClientBase.cs" Condition="Exists('../bannou-service/ServiceClients/DaprServiceClientBase.cs')" />
    <Compile Include="../bannou-service/Services/IServiceAppMappingResolver.cs" Condition="Exists('../bannou-service/Services/IServiceAppMappingResolver.cs')" />
    <Compile Include="../bannou-service/ApiException.cs" Condition="Exists('../bannou-service/ApiException.cs')" />
  </ItemGroup>

  <!-- WebSocket Binary Protocol (Client Mode Support) -->
  <!-- These are dependency-free classes for client-side WebSocket communication -->
  <ItemGroup>
    <Compile Include="../lib-connect/Protocol/BinaryMessage.cs" Condition="Exists('../lib-connect/Protocol/BinaryMessage.cs')" />
    <Compile Include="../lib-connect/Protocol/MessageFlags.cs" Condition="Exists('../lib-connect/Protocol/MessageFlags.cs')" />
    <Compile Include="../lib-connect/Protocol/NetworkByteOrder.cs" Condition="Exists('../lib-connect/Protocol/NetworkByteOrder.cs')" />
    <Compile Include="../lib-connect/Protocol/GuidGenerator.cs" Condition="Exists('../lib-connect/Protocol/GuidGenerator.cs')" />
    <Compile Include="../lib-connect/Protocol/ConnectionState.cs" Condition="Exists('../lib-connect/Protocol/ConnectionState.cs')" />
  </ItemGroup>

</Project>
EOF
```

**Files to EXCLUDE** (server-side only - do NOT add these):
- `MessageRouter.cs` - Server-side routing logic, depends on ASP.NET Core
- `BinaryProtocolTests.cs` - Unit tests, not SDK content

### Phase 2: Create BannouClient Class (New File)

**Location**: `Bannou.Client.SDK/BannouClient.cs` (manually created, not generated)

**UNCERTAINTY**: The exact API surface is an educated guess based on edge-tester patterns. May need adjustment based on actual Stride integration requirements.

**Proposed Interface**:
```csharp
namespace BeyondImmersion.Bannou.Client.SDK;

/// <summary>
/// High-level client for connecting to Bannou services via WebSocket.
/// Handles authentication, connection lifecycle, and message correlation.
/// </summary>
public class BannouClient : IAsyncDisposable
{
    private ClientWebSocket? _webSocket;
    private ConnectionState? _connectionState;
    private readonly Dictionary<ulong, TaskCompletionSource<BinaryMessage>> _pendingRequests;

    /// <summary>
    /// Connects to a Bannou server using username/password authentication.
    /// </summary>
    /// <param name="serverUrl">Base URL (e.g., "https://game.example.com")</param>
    /// <param name="username">Account username</param>
    /// <param name="password">Account password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    public Task<bool> ConnectAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects using an existing JWT token.
    /// </summary>
    public Task<bool> ConnectWithTokenAsync(
        string serverUrl,
        string jwtToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a service method and awaits the response.
    /// </summary>
    /// <typeparam name="TRequest">Request model type (generated)</typeparam>
    /// <typeparam name="TResponse">Response model type (generated)</typeparam>
    /// <param name="serviceName">Service name (e.g., "accounts", "auth")</param>
    /// <param name="methodPath">API method path (e.g., "get-account")</param>
    /// <param name="request">Request payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from service</returns>
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string serviceName,
        string methodPath,
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a fire-and-forget event (no response expected).
    /// </summary>
    public Task SendEventAsync<TRequest>(
        string serviceName,
        string methodPath,
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to server-initiated events.
    /// </summary>
    public void OnEvent<TEvent>(string eventType, Action<TEvent> handler);

    /// <summary>
    /// Current connection state.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Available services with their client-salted GUIDs.
    /// Populated after successful connection.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> AvailableServices { get; }

    /// <summary>
    /// Disconnects and cleans up resources.
    /// </summary>
    public ValueTask DisposeAsync();
}
```

### Phase 3: Authentication Helper Classes

**Location**: `Bannou.Client.SDK/Auth/` directory (manually created)

**UNCERTAINTY**: These patterns are based on edge-tester implementation. The actual auth flow may vary based on OAuth providers.

**Proposed Files**:

```csharp
// Bannou.Client.SDK/Auth/BannouAuthHelper.cs
namespace BeyondImmersion.Bannou.Client.SDK.Auth;

/// <summary>
/// Helper for authenticating with Bannou auth service.
/// </summary>
public static class BannouAuthHelper
{
    /// <summary>
    /// Performs username/password login and returns JWT token.
    /// </summary>
    public static Task<AuthResult> LoginAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new account and returns JWT token.
    /// </summary>
    public static Task<AuthResult> RegisterAsync(
        string serverUrl,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an expiring JWT token.
    /// </summary>
    public static Task<AuthResult> RefreshTokenAsync(
        string serverUrl,
        string currentToken,
        CancellationToken cancellationToken = default);
}

public class AuthResult
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
```

## Dependency Analysis

### Current SDK Dependencies (from .csproj)

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
<PackageReference Include="Dapr.Client" Version="1.15.1" />
```

### New Dependencies Required

**None** - The Protocol classes only use:
- `System.Text.Encoding` (built-in)
- `System.Security.Cryptography.SHA256` (built-in)
- `System.Buffers.Binary.BinaryPrimitives` (built-in since .NET Standard 2.1)

**UNCERTAINTY**: `System.Net.WebSockets.Client` is already included but at version 4.3.2 (old). May want to verify this works correctly with .NET 6/8/9 targets. The built-in `ClientWebSocket` in modern .NET may not need this package at all.

### Target Framework Compatibility

SDK targets: `net6.0;net8.0;net9.0`

All Protocol classes use APIs available in all these targets:
- `System.Buffers.Binary.BinaryPrimitives` - .NET Standard 2.1+ (all targets)
- `System.Security.Cryptography.SHA256.HashData()` - .NET 5+ (all targets)
- `ReadOnlyMemory<byte>` / `Span<byte>` - .NET Standard 2.1+ (all targets)

## File Inventory

### Files to Include in SDK

| Source Path | Namespace | Notes |
|-------------|-----------|-------|
| `lib-connect/Protocol/BinaryMessage.cs` | `BeyondImmersion.BannouService.Connect.Protocol` | Core message type |
| `lib-connect/Protocol/MessageFlags.cs` | `BeyondImmersion.BannouService.Connect.Protocol` | Flags enum |
| `lib-connect/Protocol/NetworkByteOrder.cs` | `BeyondImmersion.BannouService.Connect.Protocol` | Endian utilities |
| `lib-connect/Protocol/GuidGenerator.cs` | `BeyondImmersion.BannouService.Connect.Protocol` | GUID generation |
| `lib-connect/Protocol/ConnectionState.cs` | `BeyondImmersion.BannouService.Connect.Protocol` | State tracking |

### Files to Create (Not Generated)

| Target Path | Purpose |
|-------------|---------|
| `Bannou.Client.SDK/BannouClient.cs` | High-level WebSocket client |
| `Bannou.Client.SDK/Auth/BannouAuthHelper.cs` | Authentication helpers |
| `Bannou.Client.SDK/Auth/AuthResult.cs` | Auth result model |

### Files to Explicitly EXCLUDE

| Source Path | Reason |
|-------------|--------|
| `lib-connect/Protocol/MessageRouter.cs` | Server-side only, ASP.NET Core dependency |
| `lib-connect/Protocol/BinaryProtocolTests.cs` | Test file, not SDK content |

## Usage Examples

### Service Mode (Existing - No Changes)

```csharp
// For .NET services in the same network as Bannou
using BeyondImmersion.BannouService.Auth.Generated;

var authClient = new AuthClient(); // Uses Dapr internally
var result = await authClient.ValidateTokenAsync(token);
```

### Client Mode (New - After Enhancement)

```csharp
// For game clients connecting over the internet
using BeyondImmersion.Bannou.Client.SDK;

var client = new BannouClient();

// Connect (handles login + WebSocket setup + capability discovery)
await client.ConnectAsync("https://game.example.com", "player1", "password123");

// Make RPC calls (uses binary protocol internally)
var account = await client.InvokeAsync<GetAccountRequest, GetAccountResponse>(
    "accounts",
    "get-account",
    new GetAccountRequest { AccountId = myAccountId });

// Listen for server events
client.OnEvent<ServiceHeartbeatEvent>("service-heartbeat", evt => {
    Console.WriteLine($"Service {evt.ServiceName} is alive");
});

// Cleanup
await client.DisposeAsync();
```

### Mixed Mode (Stride Server + Client)

```csharp
// Stride dedicated server - uses Service Mode for backend
public class GameServer
{
    private readonly AuthClient _authClient = new();
    private readonly AccountsClient _accountsClient = new();

    public async Task<bool> ValidatePlayer(string token)
    {
        var result = await _authClient.ValidateTokenAsync(token);
        return result.Valid;
    }
}

// Stride game client - uses Client Mode for player
public class GameClient
{
    private readonly BannouClient _client = new();

    public async Task ConnectToServer(string server, string user, string pass)
    {
        await _client.ConnectAsync(server, user, pass);
    }
}
```

## Testing Strategy

### Unit Tests for Protocol Classes

The Protocol classes already have `BinaryProtocolTests.cs` in `lib-connect/Protocol/`. These tests should continue to run as part of the lib-connect test suite.

**UNCERTAINTY**: Should we duplicate these tests in the SDK test project, or rely on the lib-connect tests? Duplication ensures SDK builds correctly but increases maintenance.

### Integration Tests for BannouClient

The `edge-tester` project already tests the full flow. Once `BannouClient` is implemented, edge-tester could potentially be refactored to use it, which would serve as integration testing.

**Proposed Test Scenarios**:
1. Login with valid credentials -> Connection established
2. Login with invalid credentials -> Appropriate error
3. Send RPC request -> Receive correlated response
4. Handle server-initiated event -> Event handler called
5. Connection loss -> Auto-reconnect (if implemented)
6. Token expiration -> Refresh handling (if implemented)

## Open Questions

### 1. Namespace Decision

Protocol classes currently use `BeyondImmersion.BannouService.Connect.Protocol`. Should they:
- Keep this namespace (simpler, no changes to source files)
- Get a new namespace like `BeyondImmersion.Bannou.Client.SDK.Protocol` (cleaner SDK API)

**RECOMMENDATION**: Keep existing namespace. Changing would require maintaining separate files or using namespace aliases, adding complexity.

### 2. Auto-Reconnect Behavior

Should `BannouClient` automatically reconnect on connection loss?

**RECOMMENDATION**: Yes, but make it configurable. Game clients typically want seamless reconnection.

### 3. Message Queue / Offline Support

Should messages be queued when disconnected and sent on reconnect?

**UNCERTAINTY**: This depends on game requirements. Some games want fire-and-forget, others want guaranteed delivery. Probably should be optional.

### 4. Token Refresh

JWT tokens expire. Should `BannouClient` automatically refresh them?

**RECOMMENDATION**: Yes, transparently. Track token expiration from AuthResult and refresh proactively before expiration.

### 5. Multi-Connection Support

Should a single game client be able to have multiple `BannouClient` instances (e.g., connecting to multiple game servers)?

**UNCERTAINTY**: Probably yes for flexibility, but need to consider resource usage.

## Implementation Priority

**Priority Elevation**: This work is not just "nice to have for Stride" - it **completes the CI/CD compatibility testing loop** that's already 90% built. Without this, the backwards/forward WebSocket tests are testing model compatibility only, not protocol compatibility.

### Phase 1: Protocol Classes in SDK (30 minutes)
**Prerequisite for everything else**
- Modify `scripts/generate-client-sdk.sh` to include Protocol classes
- Run generation script and verify SDK builds
- Verify Protocol classes have no server dependencies

### Phase 2: BannouClient Implementation (1-2 days)
**Enables high-level client usage**
- Implement `BannouClient` with connection lifecycle
- Login + WebSocket connection + capability discovery
- Basic `InvokeAsync` with response correlation via MessageId
- Manual testing against edge-tester environment

### Phase 3: Edge-Tester Refactor (Critical for CI/CD - 1 day)
**Completes the compatibility testing loop**
- Refactor edge-tester to use `BannouClient` from SDK
- Make `lib-connect` reference conditional (only for development, not SDK modes)
- Verify backwards compatibility test uses SDK's Protocol classes
- Verify forward compatibility test uses SDK's Protocol classes
- **This is when the CI/CD compatibility testing becomes meaningful**

### Phase 4: Polish and Auth Helpers (1 day)
- Add `BannouAuthHelper` for standalone auth flows
- Add event subscription support
- Add auto-reconnect (configurable)
- Token refresh handling

### Future (As needed for Stride)
- Connection pooling (if needed)
- Performance optimization
- Unity/Godot compatibility (if needed - different build targets)

### Why Phase 3 is Critical

```
Current CI Flow:
  Backwards Test → edge-tester uses lib-connect directly → NOT testing SDK
  Forward Test → edge-tester uses lib-connect directly → NOT testing SDK

After Phase 3:
  Backwards Test → edge-tester uses published SDK → ACTUALLY tests old SDK vs new server
  Forward Test → edge-tester uses current SDK → ACTUALLY tests new SDK vs current server
```

Breaking changes to Protocol classes would be caught BEFORE merging to master.

## Related Documentation

- `TESTING.md` - Overall testing architecture
- `API-DESIGN.md` - Schema-first development patterns
- `WEBSOCKET-PROTOCOL.md` - Binary protocol specification (if exists)
- Edge-tester source code - Reference implementation

## Revision History

| Date | Change | Author |
|------|--------|--------|
| 2025-12-02 | Initial document created from SDK analysis | Claude |
