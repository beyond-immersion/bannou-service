# Meta Endpoints - Companion API Architecture

> **Status**: Draft / Research
> **Last Updated**: 2025-12-19 (Architecture corrected)
> **Author**: Claude Code assisted design

## Executive Summary

This document explores a "companion API" or "meta endpoint" system that allows clients to request metadata about service endpoints rather than executing them. Inspired by HTTP's `HEAD` request pattern, this feature leverages an unused bit in the WebSocket binary protocol's flags byte to enable introspection capabilities without modifying the core routing architecture.

**Key Concept**: When a specific flag bit is set, the Connect service intercepts the request and returns metadata about the targeted endpoint (identified by the same GUID) rather than forwarding it to the backend service.

---

## Table of Contents

- [Meta Endpoints - Companion API Architecture](#meta-endpoints---companion-api-architecture)
  - [Executive Summary](#executive-summary)
  - [Table of Contents](#table-of-contents)
  - [Motivation](#motivation)
  - [Technical Feasibility Analysis](#technical-feasibility-analysis)
    - [Available Flag Bit](#available-flag-bit)
    - [Routing Interception Point](#routing-interception-point)
    - [GUID Resolution](#guid-resolution)
    - [Zero-Copy Compatibility](#zero-copy-compatibility)
  - [Architecture Design](#architecture-design)
    - [Flag Definition](#flag-definition)
    - [Meta Endpoint Types](#meta-endpoint-types)
    - [Response Format](#response-format)
    - [Connect Service Handling](#connect-service-handling)
  - [Companion Endpoint Types](#companion-endpoint-types)
    - [1. Endpoint Info (`/endpoint-info`)](#1-endpoint-info-endpoint-info)
    - [2. Request Schema (`/request-schema`)](#2-request-schema-request-schema)
    - [3. Response Schema (`/response-schema`)](#3-response-schema-response-schema)
    - [4. Full Schema (`/schema`)](#4-full-schema-schema)
  - [Implementation Plan](#implementation-plan)
    - [Phase 1: Protocol Layer](#phase-1-protocol-layer)
    - [Phase 2: Connect Service Integration](#phase-2-connect-service-integration)
    - [Phase 3: Companion Controller Generation](#phase-3-companion-controller-generation)
    - [Phase 4: Model-to-JSON-Schema Conversion](#phase-4-model-to-json-schema-conversion)
    - [Phase 5: SDK Client Support](#phase-5-sdk-client-support)
    - [Phase 6: Testing](#phase-6-testing)
  - [Alternative Approaches Considered](#alternative-approaches-considered)
  - [Security Considerations](#security-considerations)
  - [Performance Considerations](#performance-considerations)
  - [Open Questions](#open-questions)
  - [References](#references)

---

## Motivation

### The Problem

1. **SDK Update Burden**: Game clients must update their SDK to get new request/response model definitions when services add new endpoints.

2. **Runtime Discovery**: Clients receive a capability manifest with GUIDs and paths, but no information about what data each endpoint expects or returns.

3. **Debugging Difficulty**: During development, understanding what an endpoint does requires reading documentation or source code.

4. **Dynamic Integrations**: AI agents or generic tools that want to interact with Bannou services have no programmatic way to discover API contracts at runtime.

### The Solution

Leverage the unused `Reserved` flag bit (0x80) to enable "meta requests" - when this bit is set, the Connect service:
1. Extracts the target GUID (same 16 bytes as a normal request)
2. Instead of forwarding to the backend service, returns metadata about that endpoint
3. The payload byte(s) specify which type of metadata is requested

This is analogous to HTTP's `HEAD` vs `GET` - same URL, different behavior based on the method/flag.

### Benefits

- **No SDK Updates Required**: Clients can fetch schemas at runtime and dynamically construct requests
- **Self-Documenting APIs**: Every endpoint becomes introspectable
- **Development Tooling**: Build generic API explorers, debuggers, and testing tools
- **AI Agent Integration**: Agents can discover and use APIs without hardcoded knowledge
- **Zero Breaking Changes**: Uses reserved protocol bit, no changes to existing message format

---

## Technical Feasibility Analysis

### Available Flag Bit

**Current MessageFlags enum** (`lib-connect/Protocol/MessageFlags.cs`):

```csharp
[Flags]
public enum MessageFlags : byte
{
    None           = 0x00,  // Default: JSON, service request, expects response
    Binary         = 0x01,  // Payload is binary (not JSON)
    Encrypted      = 0x02,  // Payload is encrypted (stub)
    Compressed     = 0x04,  // Payload is gzip compressed (stub)
    HighPriority   = 0x08,  // Skip to front of queues
    Event          = 0x10,  // Fire-and-forget, no response expected
    Client         = 0x20,  // Route to WebSocket client (P2P)
    Response       = 0x40,  // Message is a response to an RPC
    Reserved       = 0x80   // Reserved for future use  <-- AVAILABLE!
}
```

**Conclusion**: Bit 7 (0x80) is explicitly reserved and unused. Perfect for meta endpoints.

### Routing Interception Point

**Message flow in ConnectService.cs**:

```
WebSocket receives bytes
    ↓
HandleBinaryMessageAsync() (line 756-829)
    ↓
BinaryMessage.Parse() - extracts 31-byte header
    ↓
MessageRouter.AnalyzeMessage() (line 16-76 in MessageRouter.cs)
    ↓
[INTERCEPTION POINT] - Check for Meta flag BEFORE service routing
    ↓
RouteToServiceAsync() - forwards to Dapr service
```

**Best interception point**: In `HandleBinaryMessageAsync()` after parsing, before calling `MessageRouter.AnalyzeMessage()`:

```csharp
// After line 766 (after BinaryMessage.Parse()), add:
if (message.Flags.HasFlag(MessageFlags.Meta))
{
    return await HandleMetaEndpointRequestAsync(message, connectionState, ct);
}
```

This ensures meta requests never reach the router or backend services.

### GUID Resolution

Meta requests use the **same GUID** as regular requests. The ConnectionState already maintains bidirectional mappings:

```csharp
// ConnectionState.cs
public Dictionary<string, Guid> ServiceMappings { get; }  // endpoint → GUID
public Dictionary<Guid, string> GuidMappings { get; }     // GUID → endpoint (reverse)
```

When handling a meta request:
1. Extract ServiceGuid from binary header (bytes 7-22)
2. Look up endpoint key: `connectionState.TryGetServiceName(guid, out var endpointKey)`
3. Parse endpoint key: `"serviceName:METHOD:/path"` → service, method, path
4. Return metadata for that endpoint

**Conclusion**: Existing GUID infrastructure fully supports meta endpoint resolution.

### Zero-Copy Compatibility

**Key principle**: Connect service never deserializes message payloads for routing.

For meta requests:
- The payload is either **empty** or contains a **small metadata type selector**
- Connect service handles meta requests entirely internally
- No payload forwarding to backend services
- Response is generated directly by Connect service

**Conclusion**: Meta endpoints maintain zero-copy principles for regular traffic while adding minimal overhead for introspection requests.

---

## Architecture Design

### Flag Definition

Rename the `Reserved` flag to `Meta`:

```csharp
[Flags]
public enum MessageFlags : byte
{
    None           = 0x00,
    Binary         = 0x01,
    Encrypted      = 0x02,
    Compressed     = 0x04,
    HighPriority   = 0x08,
    Event          = 0x10,
    Client         = 0x20,
    Response       = 0x40,
    Meta           = 0x80   // Request metadata about endpoint instead of executing it
}
```

### Meta Endpoint Types

The payload byte(s) specify which metadata is requested:

| Byte Value | Meta Type | Description |
|------------|-----------|-------------|
| 0x00 or empty | `endpoint-info` | Human-readable endpoint description |
| 0x01 | `request-schema` | JSON Schema for request body |
| 0x02 | `response-schema` | JSON Schema for response body |
| 0x03 | `full-schema` | Complete endpoint schema (request + response + metadata) |
| 0x04-0xFF | Reserved | Future meta types |

### Response Format

All meta responses use a consistent JSON wrapper:

```json
{
  "metaType": "request-schema",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": { /* type-specific content */ },
  "generatedAt": "2025-12-19T10:30:00Z",
  "schemaVersion": "1.0.0"
}
```

### Connect Service Handling

Connect service performs **route transformation** - it does NOT generate metadata itself:

```csharp
// In RouteToServiceAsync(), detect meta flag and transform path:

private async Task RouteToServiceAsync(BinaryMessage message, ConnectionState connectionState, ...)
{
    // Resolve GUID to endpoint
    if (!connectionState.TryGetServiceName(message.ServiceGuid, out var endpointKey))
    {
        return CreateErrorResponse(message, "Unknown endpoint GUID");
    }

    // Parse endpoint key: "accounts:POST:/accounts/get"
    var parts = endpointKey.Split(':', 3);
    var serviceName = parts[0];
    var httpMethod = parts[1];
    var originalPath = parts[2];

    // META FLAG HANDLING: Transform the route to companion endpoint
    string targetPath;
    if (message.IsMeta)
    {
        var metaType = message.Payload.IsEmpty ? "info" : GetMetaTypeSuffix(message.Payload.Span[0]);
        targetPath = $"{originalPath}/meta/{metaType}";
        // Example: /accounts/get → /accounts/get/meta/schema
    }
    else
    {
        targetPath = originalPath;
    }

    // Route to service as normal - service handles the companion endpoint
    var appId = _appMappingResolver.GetAppIdForService(serviceName);
    // Forward to Dapr...
}

private static string GetMetaTypeSuffix(byte metaType) => metaType switch
{
    0x00 => "info",
    0x01 => "request-schema",
    0x02 => "response-schema",
    0x03 => "schema",
    _ => "info"  // Default fallback
};
```

**Key Principle**: Connect service stays "dumb" - it just transforms the path and routes. Each service plugin is responsible for implementing its own `/meta/*` companion endpoints.

---

## Companion Endpoint Types

### 1. Endpoint Info (`/endpoint-info`)

**Purpose**: Human-readable description of what the endpoint does.

**Response**:
```json
{
  "metaType": "endpoint-info",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": {
    "summary": "Retrieve account details by ID",
    "description": "Returns the full account profile including display name, email, and creation date. Requires authentication.",
    "tags": ["accounts", "profile"],
    "deprecated": false,
    "since": "1.0.0"
  }
}
```

**Source**: Extracted from OpenAPI schema `summary` and `description` fields.

### 2. Request Schema (`/request-schema`)

**Purpose**: JSON Schema defining the expected request body.

**Response**:
```json
{
  "metaType": "request-schema",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "required": ["accountId"],
    "properties": {
      "accountId": {
        "type": "string",
        "format": "uuid",
        "description": "The unique identifier of the account to retrieve"
      }
    }
  }
}
```

**Source**: Generated from OpenAPI `requestBody.content.application/json.schema`.

### 3. Response Schema (`/response-schema`)

**Purpose**: JSON Schema defining the response body structure.

**Response**:
```json
{
  "metaType": "response-schema",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "required": ["accountId", "email", "displayName"],
    "properties": {
      "accountId": {
        "type": "string",
        "format": "uuid"
      },
      "email": {
        "type": "string",
        "format": "email"
      },
      "displayName": {
        "type": "string",
        "minLength": 1,
        "maxLength": 64
      },
      "createdAt": {
        "type": "string",
        "format": "date-time"
      }
    }
  }
}
```

**Source**: Generated from OpenAPI `responses.200.content.application/json.schema`.

### 4. Full Schema (`/schema`)

**Purpose**: Complete endpoint documentation including request, response, and metadata.

**Response**:
```json
{
  "metaType": "full-schema",
  "endpointKey": "POST:/accounts/get",
  "serviceName": "accounts",
  "method": "POST",
  "path": "/accounts/get",
  "data": {
    "info": {
      "summary": "Retrieve account details by ID",
      "description": "Returns the full account profile...",
      "tags": ["accounts", "profile"],
      "deprecated": false
    },
    "request": {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "type": "object",
      "required": ["accountId"],
      "properties": { /* ... */ }
    },
    "response": {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "type": "object",
      "properties": { /* ... */ }
    },
    "errors": {
      "400": "Invalid account ID format",
      "401": "Authentication required",
      "404": "Account not found"
    }
  }
}
```

---

## Alternative Approaches Considered

### Approach A: Separate Meta GUIDs

**Concept**: Generate a separate "meta GUID" alongside each regular GUID in the capability manifest.

```json
{
  "availableAPIs": [
    {
      "serviceGuid": "550e8400-...",
      "metaGuid": "660f9511-...",
      "method": "POST",
      "path": "/accounts/get"
    }
  ]
}
```

**Pros**:
- No new flag needed
- Clean separation in routing
- Explicit in the capability manifest

**Cons**:
- Doubles the size of GUID mappings
- Increases manifest payload size significantly
- Requires client to track two GUIDs per endpoint
- More complex GUID generation logic

**Decision**: Rejected in favor of flag-based approach for simplicity.

### Approach B: HTTP-Only Meta Endpoints

**Concept**: Expose meta endpoints via HTTP only (e.g., `/connect/meta/{method}/{path}`).

```
GET /connect/meta/POST/accounts/get?type=request-schema
```

**Pros**:
- Simpler to implement (standard HTTP routing)
- Works with standard HTTP tooling (curl, Postman)
- No protocol changes needed

**Cons**:
- Requires HTTP authentication flow (not WebSocket session)
- Doesn't leverage existing GUID-based security model
- Can't be used by WebSocket-only clients
- Inconsistent with WebSocket-first architecture

**Decision**: Could be added later as a secondary access method, but WebSocket flag-based approach is primary.

### Approach C: Inline Schema in Capability Manifest

**Concept**: Include full schemas directly in the capability manifest.

**Cons**:
- Massively increases manifest size (schemas can be 10KB+ per endpoint)
- Most clients don't need schemas at all
- Wastes bandwidth for every connection
- Breaks progressive disclosure principle

**Decision**: Rejected - on-demand schema fetching is more efficient.

### Approach D: Embedded Resources in Connect Service

**Concept**: Extract JSON schemas at build time and embed them as assembly resources in Connect service.

**Cons**:
- Connect service becomes responsible for knowing all schemas
- Violates separation of concerns (Connect should just route)
- Makes Connect service larger and more complex
- Requires schema regeneration when any service changes
- Breaks the plugin architecture (plugins own their own metadata)

**Decision**: Rejected - each service should serve its own metadata via companion endpoints.

### Approach E: Batch Schema Endpoint

**Concept**: Special meta type (0x10) that returns schemas for ALL accessible endpoints at once.

**Cons**:
- Connect service would need to make simultaneous requests to ALL accessible endpoints
- Wait for all responses and collect them
- Immense payload size
- 99% of clients wouldn't need most of the data
- Multiplies implementation complexity substantially

**Decision**: Rejected - individual endpoint queries are sufficient and more efficient.

### Approach F: Permissions Meta Endpoint

**Concept**: Meta endpoint to return `x-permissions` requirements for an endpoint.

**Cons**:
- Clients can only see endpoints they already have access to (via capability manifest)
- Would only show "why you're allowed" - leaking internal implementation
- Provides no actionable value to the client

**Decision**: Rejected - if you can see the GUID, you already have access.

### Approach G: Example Request Endpoint

**Concept**: Meta endpoint returning example valid request payloads.

**Cons**:
- Difficult to generate realistic examples without manual effort
- Would require additional schema annotations (tags for formats)
- Auto-generated examples would contain mostly nonsense (0s and Xs)
- Manual example creation per endpoint is not scalable

**Decision**: Rejected for initial implementation - may revisit if schema annotations mature.

---

## Implementation Plan

### Phase 1: Protocol Layer

**Files to modify**:
- `lib-connect/Protocol/MessageFlags.cs`
- `lib-connect/Protocol/BinaryMessage.cs` (add helper property)

**Changes**:

1. Rename `Reserved` to `Meta` in MessageFlags enum
2. Add `IsMeta` property to BinaryMessage
3. Define `MetaType` enum for payload interpretation

```csharp
// MessageFlags.cs
Meta = 0x80   // Request metadata about endpoint instead of executing it

// BinaryMessage.cs
public bool IsMeta => Flags.HasFlag(MessageFlags.Meta);

// New file: MetaType.cs
public enum MetaType : byte
{
    EndpointInfo = 0x00,
    RequestSchema = 0x01,
    ResponseSchema = 0x02,
    FullSchema = 0x03
}
```

**Tests**: Update `BinaryProtocolTests.cs` with meta flag tests.

### Phase 2: Connect Service Integration

**Files to modify**:
- `lib-connect/ConnectService.cs`

**Changes**:

1. Add meta request handler method
2. Intercept meta requests in `HandleBinaryMessageAsync()`
3. Generate metadata responses

```csharp
// In HandleBinaryMessageAsync(), after BinaryMessage.Parse():
if (message.IsMeta)
{
    var response = await HandleMetaEndpointRequestAsync(message, connectionState, ct);
    await SendBinaryResponseAsync(webSocket, response, ct);
    return;
}

// New private method
private async Task<BinaryMessage> HandleMetaEndpointRequestAsync(
    BinaryMessage message,
    ConnectionState connectionState,
    CancellationToken ct)
{
    // Implementation as described in Architecture Design section
}
```

### Phase 3: Companion Controller Generation

**Architecture**: Each service plugin generates its own `/meta/*` companion endpoints. Connect service simply routes to them via path transformation.

**Key Principle**: Services own their metadata. Generated models already contain all the type information - companion endpoints convert these models back to JSON Schema format on demand.

**Implementation**:

1. **NSwag template modification** (`templates/nswag/`):
   - Extend controller generation to include companion endpoints for each operation
   - Generate `/meta/info`, `/meta/request-schema`, `/meta/response-schema`, `/meta/schema` endpoints

2. **Generated companion controller pattern**:
   ```csharp
   // Generated alongside regular controller in lib-{service}/Generated/

   // Regular endpoint
   [HttpPost("/accounts/get")]
   public async Task<ActionResult<AccountResponse>> GetAccount([FromBody] GetAccountRequest request)
       => await _service.GetAccountAsync(request);

   // Companion endpoints (auto-generated)
   [HttpGet("/accounts/get/meta/info")]
   public ActionResult<MetaInfoResponse> GetAccount_MetaInfo()
       => MetaGenerator.GetEndpointInfo<GetAccountRequest, AccountResponse>("Get account by ID");

   [HttpGet("/accounts/get/meta/request-schema")]
   public ActionResult<JsonSchemaResponse> GetAccount_MetaRequestSchema()
       => MetaGenerator.GetRequestSchema<GetAccountRequest>();

   [HttpGet("/accounts/get/meta/response-schema")]
   public ActionResult<JsonSchemaResponse> GetAccount_MetaResponseSchema()
       => MetaGenerator.GetResponseSchema<AccountResponse>();

   [HttpGet("/accounts/get/meta/schema")]
   public ActionResult<FullSchemaResponse> GetAccount_MetaFullSchema()
       => MetaGenerator.GetFullSchema<GetAccountRequest, AccountResponse>("Get account by ID");
   ```

3. **Shared MetaGenerator utility** (`bannou-service/Meta/MetaGenerator.cs`):
   ```csharp
   public static class MetaGenerator
   {
       /// <summary>
       /// Converts a C# model type to JSON Schema using reflection.
       /// </summary>
       public static JsonSchemaResponse GetRequestSchema<TRequest>()
       {
           var schema = JsonSchemaBuilder.FromType<TRequest>();
           return new JsonSchemaResponse { Data = schema };
       }

       public static JsonSchemaResponse GetResponseSchema<TResponse>()
       {
           var schema = JsonSchemaBuilder.FromType<TResponse>();
           return new JsonSchemaResponse { Data = schema };
       }

       public static MetaInfoResponse GetEndpointInfo<TRequest, TResponse>(string summary)
       {
           return new MetaInfoResponse
           {
               Summary = summary,
               RequestType = typeof(TRequest).Name,
               ResponseType = typeof(TResponse).Name
           };
       }

       public static FullSchemaResponse GetFullSchema<TRequest, TResponse>(string summary)
       {
           return new FullSchemaResponse
           {
               Info = GetEndpointInfo<TRequest, TResponse>(summary),
               Request = JsonSchemaBuilder.FromType<TRequest>(),
               Response = JsonSchemaBuilder.FromType<TResponse>()
           };
       }
   }
   ```

### Phase 4: Model-to-JSON-Schema Conversion

**Key Component**: `JsonSchemaBuilder` - converts C# types to JSON Schema at runtime.

**Files to create**:
- `bannou-service/Meta/JsonSchemaBuilder.cs`
- `bannou-service/Meta/MetaResponseModels.cs`

**JsonSchemaBuilder implementation**:
```csharp
// bannou-service/Meta/JsonSchemaBuilder.cs
public static class JsonSchemaBuilder
{
    /// <summary>
    /// Generates JSON Schema from a C# type using reflection and System.Text.Json attributes.
    /// </summary>
    public static JsonSchema FromType<T>() => FromType(typeof(T));

    public static JsonSchema FromType(Type type)
    {
        var schema = new JsonSchema
        {
            Schema = "http://json-schema.org/draft-07/schema#",
            Type = GetJsonType(type)
        };

        if (type.IsClass && type != typeof(string))
        {
            schema.Properties = new Dictionary<string, JsonSchema>();
            schema.Required = new List<string>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propName = GetJsonPropertyName(prop);
                schema.Properties[propName] = FromType(prop.PropertyType);

                if (IsRequired(prop))
                    schema.Required.Add(propName);
            }
        }

        return schema;
    }

    private static string GetJsonType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(Guid)) return "string"; // format: uuid
        if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type)) return "array";
        return "object";
    }

    private static string GetJsonPropertyName(PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
    }

    private static bool IsRequired(PropertyInfo prop)
    {
        // Check for Required attribute or non-nullable reference type
        return prop.GetCustomAttribute<RequiredAttribute>() != null
            || (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null);
    }
}
```

**Benefits of this approach**:
- Each service generates its own companion endpoints
- No central schema registry needed
- Models are the source of truth (already generated from OpenAPI)
- Runtime conversion means schemas always match actual request/response types
- Connect service stays simple (just route transformation)

### Phase 5: SDK Client Support

**Files to modify**:
- `sdk-sources/BannouClient.cs`
- New file: `sdk-sources/MetaTypes.cs` (model classes)

**Changes**:

1. **Add MetaType enum** (mirrors server-side):
   ```csharp
   // sdk-sources/MetaTypes.cs
   public enum MetaType : byte
   {
       EndpointInfo = 0x00,
       RequestSchema = 0x01,
       ResponseSchema = 0x02,
       FullSchema = 0x03
   }
   ```

2. **Add meta request method to BannouClient** (follows existing InvokeAsync pattern):
   ```csharp
   /// <summary>
   /// Requests metadata about an endpoint instead of executing it.
   /// Uses the Meta flag (0x80) which triggers route transformation at Connect service.
   /// Connect appends /meta/{type} to the path and routes to the service's companion endpoint.
   /// </summary>
   public async Task<MetaResponse<T>> GetEndpointMetaAsync<T>(
       string method,
       string path,
       MetaType metaType = MetaType.FullSchema,
       TimeSpan? timeout = null,
       CancellationToken cancellationToken = default)
   {
       if (_webSocket?.State != WebSocketState.Open)
           throw new InvalidOperationException("WebSocket is not connected.");
       if (_connectionState == null)
           throw new InvalidOperationException("Connection state not initialized.");

       // Get service GUID (same as regular requests)
       var serviceGuid = GetServiceGuid(method, path);
       if (serviceGuid == null)
           throw new ArgumentException($"Unknown endpoint: {method} {path}");

       // Create meta message - payload is just the MetaType byte
       var messageId = GuidGenerator.GenerateMessageId();
       var sequenceNumber = _connectionState.GetNextSequenceNumber(0);

       var message = new BinaryMessage(
           flags: MessageFlags.Meta,  // <-- Connect transforms route based on this flag
           channel: 0,
           sequenceNumber: sequenceNumber,
           serviceGuid: serviceGuid.Value,
           messageId: messageId,
           payload: new byte[] { (byte)metaType });

       // Set up response awaiter (same pattern as InvokeAsync)
       var tcs = new TaskCompletionSource<BinaryMessage>(
           TaskCreationOptions.RunContinuationsAsynchronously);
       _pendingRequests[messageId] = tcs;

       try
       {
           await _webSocket.SendAsync(
               new ArraySegment<byte>(message.ToByteArray()),
               WebSocketMessageType.Binary,
               endOfMessage: true,
               cancellationToken);

           var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
           using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
           timeoutCts.CancelAfter(effectiveTimeout);

           var response = await tcs.Task.WaitAsync(timeoutCts.Token);
           var responseJson = response.GetJsonPayload();

           return JsonSerializer.Deserialize<MetaResponse<T>>(responseJson)
               ?? throw new InvalidOperationException("Failed to deserialize meta response");
       }
       finally
       {
           _pendingRequests.TryRemove(messageId, out _);
       }
   }
   ```

3. **Convenience methods**:
   ```csharp
   public Task<MetaResponse<EndpointInfoData>> GetEndpointInfoAsync(
       string method, string path, CancellationToken ct = default)
       => GetEndpointMetaAsync<EndpointInfoData>(method, path, MetaType.EndpointInfo, null, ct);

   public Task<MetaResponse<JsonSchemaData>> GetRequestSchemaAsync(
       string method, string path, CancellationToken ct = default)
       => GetEndpointMetaAsync<JsonSchemaData>(method, path, MetaType.RequestSchema, null, ct);

   public Task<MetaResponse<JsonSchemaData>> GetResponseSchemaAsync(
       string method, string path, CancellationToken ct = default)
       => GetEndpointMetaAsync<JsonSchemaData>(method, path, MetaType.ResponseSchema, null, ct);

   public Task<MetaResponse<FullSchemaData>> GetFullSchemaAsync(
       string method, string path, CancellationToken ct = default)
       => GetEndpointMetaAsync<FullSchemaData>(method, path, MetaType.FullSchema, null, ct);
   ```

4. **Response model classes**:
   ```csharp
   // sdk-sources/MetaTypes.cs
   public class MetaResponse<T>
   {
       public string MetaType { get; set; } = "";
       public string EndpointKey { get; set; } = "";
       public string ServiceName { get; set; } = "";
       public string Method { get; set; } = "";
       public string Path { get; set; } = "";
       public T? Data { get; set; }
       public DateTimeOffset GeneratedAt { get; set; }
       public string SchemaVersion { get; set; } = "";
   }

   public class EndpointInfoData
   {
       public string Summary { get; set; } = "";
       public string Description { get; set; } = "";
       public List<string> Tags { get; set; } = new();
       public bool Deprecated { get; set; }
       public string? Since { get; set; }
   }

   public class JsonSchemaData
   {
       [JsonPropertyName("$schema")]
       public string Schema { get; set; } = "";
       public string Type { get; set; } = "";
       public List<string>? Required { get; set; }
       public Dictionary<string, JsonElement>? Properties { get; set; }
   }
   ```

### Phase 6: Testing

**Test Categories**:

1. **Unit Tests** (`lib-connect.tests/`):
   - Meta flag parsing in BinaryMessage
   - Route transformation logic
   - MetaType byte interpretation

2. **Unit Tests** (`bannou-service/`):
   - JsonSchemaBuilder type conversion
   - MetaGenerator response formatting
   - Property name mapping (camelCase)

3. **HTTP Integration Tests** (`http-tester/`):
   - Direct companion endpoint testing: `GET /accounts/get/meta/schema`
   - Verify generated companion controllers work
   - Test all meta types via HTTP (bypasses WebSocket for debugging)

4. **Edge Tests** (`edge-tester/`):
   - Full meta endpoint flow via WebSocket with Meta flag
   - All meta types (endpoint-info, request-schema, response-schema, full-schema)
   - Unknown GUID handling
   - Invalid meta type handling

**Edge Test Examples**:

```csharp
// edge-tester/Tests/MetaEndpointTestHandler.cs

public class MetaEndpointTestHandler : IServiceTestHandler
{
    public string ServiceName => "meta";
    public string DisplayName => "Meta Endpoint Tests";

    // === Endpoint Info Tests ===

    [Fact]
    public async Task Meta_GetEndpointInfo_ReturnsDescription()
    {
        var client = await CreateAuthenticatedClientAsync();

        var info = await client.GetEndpointInfoAsync("POST", "/accounts/get");

        Assert.Equal("endpoint-info", info.MetaType);
        Assert.Equal("POST:/accounts/get", info.EndpointKey);
        Assert.NotEmpty(info.Data!.Summary);
    }

    [Fact]
    public async Task Meta_GetEndpointInfo_EmptyPayload_DefaultsToInfo()
    {
        // When payload is empty, should default to endpoint-info
        var client = await CreateAuthenticatedClientAsync();
        var guid = client.GetServiceGuid("POST", "/accounts/get");

        // Send meta request with empty payload
        var response = await client.SendRawMetaRequestAsync(guid!.Value, Array.Empty<byte>());

        Assert.Equal("endpoint-info", response.MetaType);
    }

    // === Schema Tests ===

    [Fact]
    public async Task Meta_GetRequestSchema_ReturnsValidJsonSchema()
    {
        var client = await CreateAuthenticatedClientAsync();

        var schema = await client.GetRequestSchemaAsync("POST", "/accounts/get");

        Assert.Equal("request-schema", schema.MetaType);
        Assert.Equal("http://json-schema.org/draft-07/schema#", schema.Data!.Schema);
        Assert.Equal("object", schema.Data.Type);
        Assert.Contains("accountId", schema.Data.Properties!.Keys);
    }

    [Fact]
    public async Task Meta_GetResponseSchema_ReturnsExpectedFields()
    {
        var client = await CreateAuthenticatedClientAsync();

        var schema = await client.GetResponseSchemaAsync("POST", "/accounts/get");

        Assert.Equal("response-schema", schema.MetaType);
        Assert.Contains("accountId", schema.Data!.Properties!.Keys);
        Assert.Contains("email", schema.Data.Properties.Keys);
        Assert.Contains("displayName", schema.Data.Properties.Keys);
    }

    [Fact]
    public async Task Meta_GetFullSchema_IncludesAllComponents()
    {
        var client = await CreateAuthenticatedClientAsync();

        var full = await client.GetFullSchemaAsync("POST", "/accounts/get");

        Assert.Equal("full-schema", full.MetaType);
        Assert.NotNull(full.Data!.Info);
        Assert.NotNull(full.Data.Request);
        Assert.NotNull(full.Data.Response);
    }

    // === Error Handling Tests ===

    [Fact]
    public async Task Meta_UnknownGuid_ReturnsNotFound()
    {
        var client = await CreateAuthenticatedClientAsync();
        var unknownGuid = Guid.NewGuid();

        var response = await client.SendRawMetaRequestAsync(
            unknownGuid,
            new byte[] { (byte)MetaType.EndpointInfo });

        // Should return error response with NotFound status
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Meta_InvalidMetaType_DefaultsToInfo()
    {
        // Invalid meta type bytes should default to endpoint-info (graceful fallback)
        var client = await CreateAuthenticatedClientAsync();
        var guid = client.GetServiceGuid("POST", "/accounts/get");

        // Send unknown meta type byte
        var response = await client.SendRawMetaRequestAsync(
            guid!.Value,
            new byte[] { 0xFF });

        // Should fall back to info rather than error
        Assert.Equal("endpoint-info", response.MetaType);
    }

    [Fact]
    public async Task Meta_UnauthenticatedClient_CannotAccessMeta()
    {
        // Meta requires same auth as regular requests - GUID not in capability manifest
        var client = CreateUnauthenticatedClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetEndpointInfoAsync("POST", "/accounts/get"));
    }

    // === Routing Tests ===

    [Fact]
    public async Task Meta_RouteTransformation_PathCorrectlyAppended()
    {
        // Verify the route transformation: /accounts/get → /accounts/get/meta/schema
        var client = await CreateAuthenticatedClientAsync();

        // Request full schema
        var schema = await client.GetFullSchemaAsync("POST", "/accounts/get");

        // The fact we got a valid schema response proves routing worked
        Assert.Equal("full-schema", schema.MetaType);
        Assert.Equal("/accounts/get", schema.Path);
    }

    [Fact]
    public async Task Meta_SchemaVersion_IsConsistent()
    {
        var client = await CreateAuthenticatedClientAsync();

        var info = await client.GetEndpointInfoAsync("POST", "/accounts/get");
        var schema = await client.GetRequestSchemaAsync("POST", "/accounts/get");

        Assert.Equal(info.SchemaVersion, schema.SchemaVersion);
    }
}
```

**Test Coverage Matrix**:

| Meta Type | Happy Path | Error Cases | Edge Cases |
|-----------|------------|-------------|------------|
| endpoint-info | Summary present | Unknown GUID | Empty payload defaults to info |
| request-schema | Valid JSON Schema | Invalid GUID | Complex nested schemas |
| response-schema | Expected fields | Missing endpoint | Nullable properties |
| full-schema | All components | - | Deprecated endpoints |

---

## Security Considerations

### Permission Requirements

Meta endpoints should follow the same permission model as their target endpoints:

- If user can't access `POST /accounts/get`, they shouldn't see its schema
- Connect service already validates GUID ownership via session-salted mappings
- Only GUIDs in the client's capability manifest are valid

**Implementation**: The GUID lookup in `HandleMetaEndpointRequestAsync()` inherently validates access - if the GUID isn't in `connectionState.GuidMappings`, the request fails.

### Information Disclosure

**Risk**: Schemas may reveal internal implementation details.

**Mitigation**:
- Only expose schemas for endpoints the client can already access
- Don't include internal field names or comments in schemas
- Schema extraction should sanitize sensitive descriptions

### Rate Limiting

**Risk**: Meta requests could be used for reconnaissance or DoS.

**Mitigation**:
- Meta requests count toward normal rate limits
- Consider lower rate limit for meta requests specifically
- Monitor for unusual meta request patterns

---

## Performance Considerations

### Response Generation

**Per-request overhead**:
- GUID lookup: O(1) hash table lookup in ConnectionState
- Route transformation: String concatenation (negligible)
- Model-to-schema conversion: Reflection-based, ~1-5ms for typical models

**Compared to normal requests**: Meta requests have similar latency since they:
1. Still route through Connect service
2. Still make Dapr service invocation
3. Service generates schema via reflection

### Caching Strategies

**Server-side caching** (recommended):
```csharp
// In MetaGenerator - cache schema results by type
private static readonly ConcurrentDictionary<Type, JsonSchema> _schemaCache = new();

public static JsonSchema FromType<T>()
{
    return _schemaCache.GetOrAdd(typeof(T), type => BuildSchemaFromType(type));
}
```

**Client-side caching**: Clients should cache schemas locally:
```csharp
// SDK could cache schemas by endpoint key
private readonly ConcurrentDictionary<string, CachedSchema> _schemaCache = new();

public async Task<JsonSchemaData> GetRequestSchemaAsync(string method, string path, ...)
{
    var cacheKey = $"{method}:{path}:request";
    if (_schemaCache.TryGetValue(cacheKey, out var cached))
        return cached;

    var schema = await FetchSchemaFromServer(method, path, MetaType.RequestSchema);
    _schemaCache[cacheKey] = schema;
    return schema;
}
```

### Reflection Cost Mitigation

Since companion endpoints use reflection to convert models to JSON Schema:
- Cache schema results by Type (schemas don't change at runtime)
- First request per type incurs reflection cost
- Subsequent requests return cached schema instantly
- Consider pre-warming cache at service startup for common types

---

## Open Questions

1. **Schema format**: JSON Schema draft-07 vs OpenAPI 3.1 native schemas?
   - JSON Schema is more universal for validation
   - OpenAPI schemas include extensions like `x-permissions`
   - Decision: Start with JSON Schema draft-07 for maximum compatibility

2. **Versioning**: How to handle schema versions when services update?
   - Include `schemaVersion` in response
   - Consider `If-Modified-Since` style freshness checking
   - Version tied to assembly version of the service plugin

3. **HTTP access to companion endpoints**: Should meta endpoints be directly accessible via HTTP?
   - Companion endpoints are already HTTP endpoints (generated controllers)
   - Direct HTTP: `GET /accounts/get/meta/schema` works without WebSocket
   - WebSocket flag just provides route transformation convenience
   - Decision: Both access patterns work naturally

4. **Service-specific metadata**: What if services want to expose custom metadata?
   - Companion endpoints are generated per-service - services can add custom meta endpoints
   - Custom meta types would need SDK updates
   - Decision: Start with standard types, expand based on need

5. **NSwag template complexity**: How complex will the companion endpoint generation template be?
   - Need to generate 4 additional endpoints per operation
   - Must handle request-only endpoints (no response schema)
   - Must handle response-only endpoints (no request body)
   - Decision: Prototype template modifications before committing

---

## References

- [WEBSOCKET-PROTOCOL.md](WEBSOCKET-PROTOCOL.md) - Binary protocol specification
- [lib-connect/Protocol/](../lib-connect/Protocol/) - Protocol implementation
- [JSON Schema Specification](https://json-schema.org/) - Schema format reference
- [OpenAPI 3.0 Specification](https://spec.openapis.org/oas/v3.0.3) - Source schema format
- HTTP `HEAD` method - [RFC 7231 Section 4.3.2](https://datatracker.ietf.org/doc/html/rfc7231#section-4.3.2)

---

## Implementation Checklist

### Phase 1: Protocol Layer
- [ ] Rename `Reserved` to `Meta` in `MessageFlags.cs`
- [ ] Add `IsMeta` property to `BinaryMessage.cs`
- [ ] Create `MetaType.cs` enum in `lib-connect/Protocol/`
- [ ] Update `BinaryProtocolTests.cs`
- [ ] Update WEBSOCKET-PROTOCOL.md documentation

### Phase 2: Connect Service Route Transformation
- [ ] Add meta flag detection in `RouteToServiceAsync()`
- [ ] Implement `GetMetaTypeSuffix()` method
- [ ] Add path transformation: `{path}` → `{path}/meta/{type}`
- [ ] Add unit tests for route transformation logic

### Phase 3: Companion Controller Generation
- [ ] Create/modify NSwag template for companion endpoint generation
- [ ] Template generates `/meta/info`, `/meta/request-schema`, `/meta/response-schema`, `/meta/schema` per operation
- [ ] Handle endpoints with no request body (skip request-schema)
- [ ] Handle endpoints with no response body (skip response-schema)
- [ ] Test generation with sample schema

### Phase 4: Model-to-JSON-Schema Conversion
- [ ] Create `bannou-service/Meta/JsonSchemaBuilder.cs`
- [ ] Create `bannou-service/Meta/MetaGenerator.cs`
- [ ] Create `bannou-service/Meta/MetaResponseModels.cs`
- [ ] Implement Type→JsonSchema reflection conversion
- [ ] Add server-side caching for converted schemas
- [ ] Handle nullable types, arrays, nested objects
- [ ] Add unit tests for JsonSchemaBuilder

### Phase 5: SDK Client Support
- [ ] Add `GetEndpointMetaAsync<T>()` to `BannouClient.cs`
- [ ] Add convenience methods for each meta type
- [ ] Create `sdk-sources/MetaTypes.cs` with response models
- [ ] Add client-side schema caching
- [ ] Update SDK tests

### Phase 6: Testing
- [ ] Create `MetaEndpointTestHandler.cs` in edge-tester
- [ ] Test all meta types via WebSocket
- [ ] Test direct HTTP access to companion endpoints
- [ ] Test error cases (unknown GUID, missing companion endpoint)
- [ ] Test schema validation (verify JSON Schema is valid)
- [ ] Verify caching behavior

---

*This document is a living specification. Update as implementation progresses and questions are resolved.*
