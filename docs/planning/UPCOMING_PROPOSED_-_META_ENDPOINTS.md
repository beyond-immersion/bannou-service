# Meta Endpoints - Companion API Architecture

> **Status**: Draft / Research
> **Last Updated**: 2025-12-22 (All questions resolved, Channel-based meta type encoding)
> **Author**: Claude Code assisted design

## Executive Summary

This document explores a "companion API" or "meta endpoint" system that allows clients to request metadata about service endpoints rather than executing them. Inspired by HTTP's `HEAD` request pattern, this feature leverages an unused bit in the WebSocket binary protocol's flags byte to enable introspection capabilities without modifying the core routing architecture.

**Key Concept**: When a specific flag bit is set, the Connect service intercepts the request and returns metadata about the targeted endpoint (identified by the same GUID) rather than forwarding it to the backend service.

**Implementation Recommendation**: Static Schema Generation (Approach B) - embed schema strings directly in generated controllers at NSwag generation time, preserving full OpenAPI schema fidelity with zero runtime overhead.

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
  - [Implementation Approaches Analysis](#implementation-approaches-analysis)
    - [Approach A: Reflection-Based (Original)](#approach-a-reflection-based-original)
    - [Approach B: Static Schema Generation (Recommended)](#approach-b-static-schema-generation-recommended)
    - [Approach C: Attribute-Based Hybrid](#approach-c-attribute-based-hybrid)
    - [Comparison Matrix](#comparison-matrix)
    - [TENETS Compliance Analysis](#tenets-compliance-analysis)
    - [Recommendation](#recommendation)
  - [Implementation Plan](#implementation-plan)
    - [Phase 1: Protocol Layer](#phase-1-protocol-layer)
    - [Phase 2: Connect Service Integration](#phase-2-connect-service-integration)
    - [Phase 3: Companion Controller Generation (Static)](#phase-3-companion-controller-generation-static)
    - [Phase 4: Response Models](#phase-4-response-models)
    - [Phase 5: SDK Client Support](#phase-5-sdk-client-support)
    - [Phase 6: Testing](#phase-6-testing)
  - [Alternative Approaches Considered](#alternative-approaches-considered)
  - [Security Considerations](#security-considerations)
  - [Performance Considerations](#performance-considerations)
  - [Open Questions - Resolved](#open-questions---resolved)
  - [Open Questions - Unresolved](#open-questions---unresolved)
  - [TENETS Compliance Checklist](#tenets-compliance-checklist)
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

**CRITICAL**: Connect service NEVER reads payloads (zero-copy routing principle). Meta type is encoded in the **header**, not the payload.

When Meta flag (0x80) is set, the **Channel field** specifies which metadata is requested:

| Channel Value | Meta Type | Description |
|---------------|-----------|-------------|
| 0 | `endpoint-info` | Human-readable endpoint description |
| 1 | `request-schema` | JSON Schema for request body |
| 2 | `response-schema` | JSON Schema for response body |
| 3 | `full-schema` | Complete endpoint schema (request + response + metadata) |
| 4-65535 | Reserved | Future meta types |

**Header Layout for Meta Requests**:
```
[Flags: 0x80 (Meta)] [Channel: 0-3 (meta type)] [Sequence] [ServiceGuid] [MessageId]
[Payload: EMPTY - never read by Connect]
```

This approach:
- Maintains zero-copy routing - Connect never deserializes payload
- Uses existing header fields - no protocol changes needed
- Provides 65536 potential meta types (only 4 used initially)
- Clients send empty payload for meta requests

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
    // Meta type is encoded in Channel field (NOT payload - zero-copy principle)
    string targetPath;
    if (message.IsMeta)
    {
        var metaType = GetMetaTypeSuffix(message.Channel);
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

private static string GetMetaTypeSuffix(ushort channel) => channel switch
{
    0 => "info",
    1 => "request-schema",
    2 => "response-schema",
    3 => "schema",
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

**Source**: Extracted from OpenAPI schema `summary` and `description` fields at generation time.

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

**Source**: Embedded directly from OpenAPI `requestBody.content.application/json.schema` at generation time.

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

**Source**: Embedded directly from OpenAPI `responses.200.content.application/json.schema` at generation time.

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

## Implementation Approaches Analysis

Three implementation approaches were evaluated for generating companion API metadata:

### Approach A: Reflection-Based (Original)

**Concept**: Generate companion endpoints that use runtime reflection to convert C# model types back to JSON Schema.

**Data Flow**:
```
OpenAPI Schema → NSwag → C# Types → Reflection → JSON Schema (runtime)
```

**Implementation**:
```csharp
// Generated companion endpoints call MetaGenerator
[HttpGet("/accounts/get/meta/request-schema")]
public ActionResult<JsonSchemaResponse> GetAccount_MetaRequestSchema()
    => MetaGenerator.GetRequestSchema<GetAccountRequest>();

// MetaGenerator uses reflection
public static class MetaGenerator
{
    public static JsonSchemaResponse GetRequestSchema<TRequest>()
    {
        var schema = JsonSchemaBuilder.FromType<TRequest>();  // Reflection here
        return new JsonSchemaResponse { Data = schema };
    }
}
```

**JsonSchemaBuilder** must handle:
- Primitive types → JSON types
- Nullable types → `"type": ["string", "null"]`
- Arrays/collections → `"type": "array"`
- Nested objects → recursive schema generation
- Enums → `"enum": [...]`
- Attributes → `[Required]`, `[JsonPropertyName]`, `[StringLength]`, etc.

**Pros**:
- Schemas always match actual runtime types
- Works with any model changes automatically
- No schema string duplication in binary

**Cons**:
- **Information Loss**: OpenAPI constraints (pattern, minLength, format) not preserved in C# types
- **Runtime Cost**: Reflection on every request (mitigated by caching)
- **Complexity**: JsonSchemaBuilder must handle all type variations
- **TENETS Violation**: C# types become the schema source, not OpenAPI (violates Tenet 1)

### Approach B: Static Schema Generation (Recommended)

**Concept**: Embed JSON Schema strings directly into generated controller code at NSwag generation time. No reflection needed - all schema information is statically known.

**Data Flow**:
```
OpenAPI Schema → NSwag Template → JSON String Literal (embedded in controller)
```

**Implementation**:
```csharp
// Generated controller with embedded schema strings
public partial class AccountsController
{
    // Regular endpoint
    [HttpPost("/accounts/get")]
    public async Task<ActionResult<AccountResponse>> GetAccount([FromBody] GetAccountRequest request)
        => await _service.GetAccountAsync(request);

    // Companion endpoints with static schema strings
    private static readonly string _getAccount_RequestSchema = """
        {
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
        """;

    private static readonly string _getAccount_ResponseSchema = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "required": ["accountId", "email", "displayName"],
          "properties": {
            "accountId": { "type": "string", "format": "uuid" },
            "email": { "type": "string", "format": "email" },
            "displayName": { "type": "string", "minLength": 1, "maxLength": 64 },
            "createdAt": { "type": "string", "format": "date-time" }
          }
        }
        """;

    private static readonly string _getAccount_Info = """
        {
          "summary": "Retrieve account details by ID",
          "description": "Returns the full account profile including display name, email, and creation date.",
          "tags": ["accounts", "profile"],
          "deprecated": false,
          "operationId": "GetAccount"
        }
        """;

    [HttpGet("/accounts/get/meta/info")]
    public ActionResult<MetaResponse> GetAccount_MetaInfo()
        => Ok(MetaResponseBuilder.BuildInfoResponse("accounts", "POST", "/accounts/get", _getAccount_Info));

    [HttpGet("/accounts/get/meta/request-schema")]
    public ActionResult<MetaResponse> GetAccount_MetaRequestSchema()
        => Ok(MetaResponseBuilder.BuildSchemaResponse("accounts", "POST", "/accounts/get", "request-schema", _getAccount_RequestSchema));

    [HttpGet("/accounts/get/meta/response-schema")]
    public ActionResult<MetaResponse> GetAccount_MetaResponseSchema()
        => Ok(MetaResponseBuilder.BuildSchemaResponse("accounts", "POST", "/accounts/get", "response-schema", _getAccount_ResponseSchema));

    [HttpGet("/accounts/get/meta/schema")]
    public ActionResult<MetaResponse> GetAccount_MetaFullSchema()
        => Ok(MetaResponseBuilder.BuildFullSchemaResponse("accounts", "POST", "/accounts/get",
            _getAccount_Info, _getAccount_RequestSchema, _getAccount_ResponseSchema));
}
```

**NSwag Template Extension** (`Controller.liquid`):
```liquid
{% for operation in Operations %}
    // === Meta endpoints for {{ operation.OperationId }} ===

    private static readonly string _{{ operation.OperationId | camel_case }}_RequestSchema = """
        {{ operation.RequestBody.Content["application/json"].Schema | to_json_schema }}
        """;

    private static readonly string _{{ operation.OperationId | camel_case }}_ResponseSchema = """
        {{ operation.Responses["200"].Content["application/json"].Schema | to_json_schema }}
        """;

    private static readonly string _{{ operation.OperationId | camel_case }}_Info = """
        {
          "summary": "{{ operation.Summary | escape_json }}",
          "description": "{{ operation.Description | escape_json }}",
          "tags": {{ operation.Tags | to_json }},
          "deprecated": {{ operation.IsDeprecated | to_json }},
          "operationId": "{{ operation.OperationId }}"
        }
        """;

    [HttpGet("{{ operation.Path }}/meta/info")]
    public ActionResult<MetaResponse> {{ operation.OperationId }}_MetaInfo()
        => Ok(MetaResponseBuilder.BuildInfoResponse("{{ ServiceName }}", "{{ operation.HttpMethod }}", "{{ operation.Path }}", _{{ operation.OperationId | camel_case }}_Info));

    // ... similar for other meta endpoints
{% endfor %}
```

**Pros**:
- **Full Schema Fidelity**: ALL OpenAPI constraints preserved (pattern, minLength, format, enum values, etc.)
- **Zero Runtime Cost**: Just return pre-computed strings
- **TENETS Compliant**: OpenAPI schema remains single source of truth (Tenet 1)
- **Simpler Infrastructure**: No JsonSchemaBuilder, no reflection utilities, no caching needed
- **Deterministic**: Same input always produces same output
- **Testable**: Schema output can be verified at build time

**Cons**:
- Increases binary size (embedded strings)
- Schema strings duplicated in memory if multiple instances (mitigated by `static readonly`)
- Requires NSwag template modification (one-time effort)

### Approach C: Attribute-Based Hybrid

**Concept**: Generate attributes on endpoints containing schema information, then use reflection to extract the attribute data (not reconstruct schemas from types).

**Data Flow**:
```
OpenAPI Schema → NSwag → Attributes with schema strings → Reflection to read attributes
```

**Implementation**:
```csharp
// Custom attributes with embedded schema data
[AttributeUsage(AttributeTargets.Method)]
public class EndpointSchemaAttribute : Attribute
{
    public string RequestSchema { get; }
    public string ResponseSchema { get; }
    public string Info { get; }

    public EndpointSchemaAttribute(string requestSchema, string responseSchema, string info)
    {
        RequestSchema = requestSchema;
        ResponseSchema = responseSchema;
        Info = info;
    }
}

// Generated controller with attributes
public partial class AccountsController
{
    [HttpPost("/accounts/get")]
    [EndpointSchema(
        requestSchema: "{\"$schema\":\"http://json-schema.org/draft-07/schema#\",...}",
        responseSchema: "{\"$schema\":\"http://json-schema.org/draft-07/schema#\",...}",
        info: "{\"summary\":\"Retrieve account details by ID\",...}"
    )]
    public async Task<ActionResult<AccountResponse>> GetAccount([FromBody] GetAccountRequest request)
        => await _service.GetAccountAsync(request);
}

// Meta endpoints extract via reflection
[HttpGet("/accounts/get/meta/request-schema")]
public ActionResult<MetaResponse> GetAccount_MetaRequestSchema()
{
    var method = typeof(AccountsController).GetMethod(nameof(GetAccount));
    var attr = method?.GetCustomAttribute<EndpointSchemaAttribute>();
    return Ok(new MetaResponse { Data = JsonDocument.Parse(attr!.RequestSchema) });
}
```

**Pros**:
- Schemas embedded at generation time (full fidelity)
- Reflection only reads attributes, doesn't reconstruct schemas
- Attributes provide documentation at the endpoint level
- Works with existing reflection-based tooling

**Cons**:
- Awkward syntax (long strings in attributes)
- Still requires reflection at runtime (though minimal)
- Attribute string limits may cause issues with large schemas
- More complex than static strings approach
- Two-step process (generate attributes, then read them) adds unnecessary complexity

### Comparison Matrix

| Criterion | A: Reflection | B: Static Generation | C: Attribute Hybrid |
|-----------|--------------|---------------------|---------------------|
| **Schema Fidelity** | Partial (loses OpenAPI constraints) | Full (preserves all constraints) | Full (preserves all constraints) |
| **Runtime Cost** | High (reflection, mitigated by cache) | Zero (return strings) | Low (attribute reflection) |
| **Infrastructure** | JsonSchemaBuilder, caching | MetaResponseBuilder only | Custom attributes, reflection |
| **TENETS Compliance** | Violates Tenet 1 | Fully compliant | Compliant |
| **Binary Size** | Minimal | Increased (embedded strings) | Increased (attribute strings) |
| **Complexity** | High (type handling) | Low (template only) | Medium |
| **Testability** | Runtime only | Build-time verification possible | Runtime only |
| **Maintenance** | JsonSchemaBuilder updates | Template updates | Attribute + extraction updates |

### TENETS Compliance Analysis

**Tenet 1: Schema-First Development** - "OpenAPI schemas are the source of truth"

| Approach | Compliance | Reasoning |
|----------|------------|-----------|
| A: Reflection | **VIOLATES** | C# types become source of truth; OpenAPI constraints lost |
| B: Static | **COMPLIANT** | OpenAPI schema embedded directly; full fidelity preserved |
| C: Hybrid | **COMPLIANT** | OpenAPI schema embedded in attributes; full fidelity preserved |

**Tenet 2: Code Generation System** - "8-component pipeline from schemas"

| Approach | Compliance | Reasoning |
|----------|------------|-----------|
| A: Reflection | Partial | Adds JsonSchemaBuilder outside pipeline |
| B: Static | **COMPLIANT** | Extends existing NSwag template pipeline |
| C: Hybrid | Partial | Adds custom attributes and reflection logic |

**Tenet 6: Service Implementation Pattern** - "Partial class requirement"

| Approach | Compliance | Reasoning |
|----------|------------|-----------|
| A: Reflection | **COMPLIANT** | Companion endpoints in separate partial class |
| B: Static | **COMPLIANT** | Companion endpoints generated alongside main controller |
| C: Hybrid | **COMPLIANT** | Attributes on main controller, extraction in partial |

**Tenet 10: Generated Code Integrity** - "Never edit generated files"

| Approach | Compliance | Reasoning |
|----------|------------|-----------|
| All | **COMPLIANT** | All approaches generate new code, don't edit existing |

### Recommendation

**Recommended Approach: B (Static Schema Generation)**

The static generation approach is superior for Bannou because:

1. **TENETS Compliance (Tenet 1)**: OpenAPI schema remains single source of truth - schema strings are extracted directly from OpenAPI YAML and embedded verbatim. No information is lost in translation.

2. **Performance**: Zero runtime cost - just return pre-computed strings. No reflection, no type analysis, no caching infrastructure needed.

3. **Simplicity**: No JsonSchemaBuilder to maintain. No reflection utilities. No caching logic. Just a template modification and a simple MetaResponseBuilder.

4. **Schema Fidelity**: ALL OpenAPI constraints preserved:
   - `pattern` (regex validation)
   - `minLength` / `maxLength`
   - `minimum` / `maximum`
   - `format` (email, uuid, date-time, etc.)
   - `enum` with all values
   - `description` on every property
   - `required` arrays
   - Nested `$ref` resolution

5. **Deterministic**: Same OpenAPI input always produces same schema output. Build-time verification is possible.

6. **Template Extension**: Fits naturally into existing NSwag template system. One template file modification enables all services.

**Binary Size Consideration**: The embedded strings increase binary size, but:
- Strings are `static readonly` (single instance per type)
- Compression (if used) is highly effective on JSON text
- Schema strings are typically 1-5KB per endpoint
- A service with 20 endpoints adds ~100KB (negligible for server-side)

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

1. Detect meta flag in `RouteToServiceAsync()`
2. Transform path to companion endpoint
3. Route to service normally (service handles companion endpoint)

```csharp
// In RouteToServiceAsync(), add path transformation for meta requests:
// Meta type is encoded in Channel field (NOT payload - Connect never reads payloads)
if (message.IsMeta)
{
    var metaType = GetMetaTypeSuffix(message.Channel);
    targetPath = $"{originalPath}/meta/{metaType}";
    // Continue routing to service as normal
}
```

### Phase 3: Companion Controller Generation (Static)

**Files to create/modify**:
- `templates/nswag/Controller.liquid` - extend with companion endpoint generation
- `scripts/generate-all-services.sh` - ensure schema extraction works

**NSwag Template Additions**:

The template must:
1. Extract request body schema from each operation
2. Extract response schema from each operation
3. Extract metadata (summary, description, tags, deprecated)
4. Generate `static readonly string` fields with embedded JSON
5. Generate companion endpoint methods that return these strings

**Key Template Logic**:

```liquid
{% for operation in Operations %}
{% if operation.HasRequestBody %}
    private static readonly string _{{ operation.OperationId | to_camel_case }}_RequestSchema = """
        {{ operation | extract_request_schema | to_json_schema_string }}
        """;
{% endif %}

{% if operation.ActualResponse %}
    private static readonly string _{{ operation.OperationId | to_camel_case }}_ResponseSchema = """
        {{ operation | extract_response_schema | to_json_schema_string }}
        """;
{% endif %}

    private static readonly string _{{ operation.OperationId | to_camel_case }}_Info = """
        {
          "summary": {{ operation.Summary | escape_json_string }},
          "description": {{ operation.Description | escape_json_string }},
          "tags": {{ operation.Tags | to_json_array }},
          "deprecated": {{ operation.IsDeprecated | to_lower }},
          "operationId": "{{ operation.OperationId }}"
        }
        """;

    /// <summary>
    /// Returns endpoint information for {{ operation.OperationId }}
    /// </summary>
    [HttpGet("{{ operation.Path }}/meta/info")]
    public ActionResult<MetaResponse> {{ operation.OperationId }}_MetaInfo()
        => Ok(MetaResponseBuilder.BuildInfoResponse(
            serviceName: "{{ ServiceName }}",
            method: "{{ operation.HttpMethod }}",
            path: "{{ operation.Path }}",
            info: _{{ operation.OperationId | to_camel_case }}_Info));

    // Similar for request-schema, response-schema, schema endpoints
{% endfor %}
```

**Schema Extraction Considerations**:

1. **$ref Resolution**: NSwag already resolves `$ref` references - use resolved schemas
2. **Inline Definitions**: Some schemas define types inline - include them
3. **Nullable Handling**: Convert nullable types to JSON Schema `["type", "null"]` format
4. **Enum Values**: Include all enum values in `enum` array
5. **Format Preservation**: Keep `format` (uuid, email, date-time, etc.)

### Phase 4: Response Models

**Files to create**:
- `bannou-service/Meta/MetaResponse.cs`
- `bannou-service/Meta/MetaResponseBuilder.cs`

**MetaResponse Model**:
```csharp
// bannou-service/Meta/MetaResponse.cs
public class MetaResponse
{
    public string MetaType { get; set; } = "";
    public string EndpointKey { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public JsonDocument? Data { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public string SchemaVersion { get; set; } = "";
}
```

**MetaResponseBuilder**:
```csharp
// bannou-service/Meta/MetaResponseBuilder.cs
public static class MetaResponseBuilder
{
    private static readonly string SchemaVersion = Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString() ?? "1.0.0";

    public static MetaResponse BuildInfoResponse(
        string serviceName, string method, string path, string infoJson)
    {
        return new MetaResponse
        {
            MetaType = "endpoint-info",
            EndpointKey = $"{method}:{path}",
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = JsonDocument.Parse(infoJson),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }

    public static MetaResponse BuildSchemaResponse(
        string serviceName, string method, string path,
        string metaType, string schemaJson)
    {
        return new MetaResponse
        {
            MetaType = metaType,
            EndpointKey = $"{method}:{path}",
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = JsonDocument.Parse(schemaJson),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }

    public static MetaResponse BuildFullSchemaResponse(
        string serviceName, string method, string path,
        string infoJson, string? requestSchemaJson, string? responseSchemaJson)
    {
        var fullSchema = new
        {
            info = JsonDocument.Parse(infoJson).RootElement,
            request = requestSchemaJson != null
                ? JsonDocument.Parse(requestSchemaJson).RootElement
                : (JsonElement?)null,
            response = responseSchemaJson != null
                ? JsonDocument.Parse(responseSchemaJson).RootElement
                : (JsonElement?)null
        };

        return new MetaResponse
        {
            MetaType = "full-schema",
            EndpointKey = $"{method}:{path}",
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = JsonDocument.Parse(JsonSerializer.Serialize(fullSchema)),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }
}
```

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
   /// Meta type is encoded in Channel field (Connect never reads payloads).
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

       // Create meta message - meta type encoded in Channel field, payload is EMPTY
       var messageId = GuidGenerator.GenerateMessageId();
       var sequenceNumber = _connectionState.GetNextSequenceNumber((ushort)metaType);

       var message = new BinaryMessage(
           flags: MessageFlags.Meta,         // <-- Meta flag triggers route transformation
           channel: (ushort)metaType,        // <-- Meta type in Channel (0=info, 1=req, 2=resp, 3=full)
           sequenceNumber: sequenceNumber,
           serviceGuid: serviceGuid.Value,
           messageId: messageId,
           payload: Array.Empty<byte>());    // <-- EMPTY - Connect never reads payloads

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

2. **Unit Tests** (`unit-tests/`):
   - MetaResponseBuilder formatting
   - MetaResponse serialization

3. **HTTP Integration Tests** (`http-tester/`):
   - Direct companion endpoint testing: `GET /accounts/get/meta/schema`
   - Verify generated companion controllers work
   - Test all meta types via HTTP (bypasses WebSocket for debugging)
   - Validate returned JSON Schema against draft-07 specification

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
    public async Task Meta_GetEndpointInfo_Channel0_ReturnsInfo()
    {
        // Channel 0 = endpoint-info (meta type encoded in header, not payload)
        var client = await CreateAuthenticatedClientAsync();
        var guid = client.GetServiceGuid("POST", "/accounts/get");

        // Send meta request with channel 0 (endpoint-info)
        var response = await client.SendRawMetaRequestAsync(guid!.Value, channel: 0);

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

    // === Schema Fidelity Tests (Static Generation Verification) ===

    [Fact]
    public async Task Meta_RequestSchema_PreservesFormatConstraints()
    {
        // Verify OpenAPI format constraints are preserved
        var client = await CreateAuthenticatedClientAsync();

        var schema = await client.GetRequestSchemaAsync("POST", "/accounts/get");

        var accountIdProp = schema.Data!.Properties!["accountId"];
        Assert.Equal("uuid", accountIdProp.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Meta_ResponseSchema_PreservesStringConstraints()
    {
        // Verify minLength/maxLength preserved from OpenAPI
        var client = await CreateAuthenticatedClientAsync();

        var schema = await client.GetResponseSchemaAsync("POST", "/accounts/get");

        var displayNameProp = schema.Data!.Properties!["displayName"];
        Assert.Equal(1, displayNameProp.GetProperty("minLength").GetInt32());
        Assert.Equal(64, displayNameProp.GetProperty("maxLength").GetInt32());
    }

    // === Error Handling Tests ===

    [Fact]
    public async Task Meta_UnknownGuid_ReturnsNotFound()
    {
        var client = await CreateAuthenticatedClientAsync();
        var unknownGuid = Guid.NewGuid();

        // Meta type in channel (0 = endpoint-info), GUID is unknown
        var response = await client.SendRawMetaRequestAsync(unknownGuid, channel: 0);

        // Should return error response with NotFound status
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Meta_InvalidChannel_DefaultsToInfo()
    {
        // Invalid channel values should default to endpoint-info (graceful fallback)
        var client = await CreateAuthenticatedClientAsync();
        var guid = client.GetServiceGuid("POST", "/accounts/get");

        // Send unknown meta type channel (99 is not a defined meta type)
        var response = await client.SendRawMetaRequestAsync(guid!.Value, channel: 99);

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

| Meta Type | Happy Path | Error Cases | Edge Cases | Fidelity |
|-----------|------------|-------------|------------|----------|
| endpoint-info (ch 0) | Summary present | Unknown GUID | Invalid channel defaults to info | Tags preserved |
| request-schema (ch 1) | Valid JSON Schema | Invalid GUID | No request body → `{}` | format, minLength preserved |
| response-schema (ch 2) | Expected fields | Missing endpoint | Nullable properties | enum values preserved |
| full-schema (ch 3) | All components | - | Deprecated endpoints | All constraints preserved |

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

## Security Considerations

### Permission Requirements

Meta endpoints should follow the same permission model as their target endpoints:

- If user can't access `POST /accounts/get`, they shouldn't see its schema
- Connect service already validates GUID ownership via session-salted mappings
- Only GUIDs in the client's capability manifest are valid

**Implementation**: The GUID lookup in route transformation inherently validates access - if the GUID isn't in `connectionState.GuidMappings`, the request fails before transformation.

### Information Disclosure

**Risk**: Schemas may reveal internal implementation details.

**Mitigation**:
- Only expose schemas for endpoints the client can already access
- Don't include internal field names or comments in schemas
- OpenAPI schemas are already designed for external consumption
- Static generation preserves only what's in OpenAPI (no runtime type leakage)

### Rate Limiting

**Risk**: Meta requests could be used for reconnaissance or DoS.

**Mitigation**:
- Meta requests count toward normal rate limits
- Consider lower rate limit for meta requests specifically
- Monitor for unusual meta request patterns

---

## Performance Considerations

### Response Generation (Static Approach)

**Per-request overhead**:
- GUID lookup: O(1) hash table lookup in ConnectionState
- Route transformation: String concatenation (negligible)
- Response generation: Return pre-computed string (zero computation)

**Compared to reflection approach**:
- No reflection overhead
- No type analysis
- No caching needed (strings are compile-time constants)

### Memory Usage

**Static schema strings**:
- Declared as `static readonly` - single instance per type
- Loaded into memory at first access, shared across all requests
- Typical schema: 1-5KB per endpoint
- Service with 20 endpoints: ~100KB additional memory (negligible)

**No runtime caching needed**:
- Reflection approach requires ConcurrentDictionary cache
- Static approach eliminates this complexity entirely

### Binary Size

**Impact**:
- Each endpoint adds ~1-5KB of embedded JSON strings
- 20 endpoints ≈ 100KB increase in assembly size
- Compression (if used in deployment) highly effective on JSON text
- Trade-off: Slightly larger binary for zero runtime cost

---

## Open Questions - Resolved

### Q1: Schema format - JSON Schema draft-07 vs OpenAPI 3.1 native schemas?

**Resolution**: Use JSON Schema draft-07.

**Rationale**:
- JSON Schema draft-07 is universally supported by validation libraries
- OpenAPI 3.0 schemas are JSON Schema draft-04 compatible (subset)
- OpenAPI 3.1 aligned with JSON Schema draft-2020-12 (compatible with draft-07)
- draft-07 provides `$schema` header for clear version identification
- Most validation libraries default to draft-07

### Q2: Versioning - How to handle schema versions when services update?

**Resolution**: Use assembly version tied to service plugin.

**Implementation**:
```csharp
private static readonly string SchemaVersion = Assembly.GetExecutingAssembly()
    .GetName().Version?.ToString() ?? "1.0.0";
```

**Rationale**:
- Schema changes require code regeneration, which updates assembly version
- Clients can cache by version and re-fetch when version changes
- No separate versioning system needed
- Version included in every MetaResponse

### Q3: HTTP access to companion endpoints - Should meta endpoints be directly accessible via HTTP?

**Resolution**: Yes, companion endpoints are standard HTTP GET endpoints.

**Behavior**:
- Direct HTTP: `GET /accounts/get/meta/schema` works without WebSocket
- WebSocket Meta flag: Triggers route transformation to same endpoint
- Both access patterns use identical generated companion endpoints
- HTTP useful for debugging, tooling, documentation generation

### Q4: Service-specific metadata - What if services want to expose custom metadata?

**Resolution**: Services can add custom meta endpoints manually.

**Approach**:
- Generated companion endpoints cover standard meta types
- Services can add additional `/meta/*` endpoints in their partial class
- Custom meta types would need SDK updates (new MetaType enum values)
- Document custom meta patterns for consistency

**Example**:
```csharp
// In lib-myservice/MyServiceController.Custom.cs (partial class)
[HttpGet("/myendpoint/meta/custom-validation-rules")]
public ActionResult<MetaResponse> MyEndpoint_CustomValidationRules()
    => Ok(new MetaResponse { MetaType = "custom-validation-rules", Data = ... });
```

### Q5: NSwag template complexity - How complex will the companion endpoint generation template be?

**Resolution**: Moderate complexity, manageable with Liquid template features.

**Key template tasks**:
1. Extract schema JSON from operation (NSwag provides this)
2. Convert to JSON string literal with proper escaping
3. Generate static readonly fields
4. Generate companion endpoint methods
5. Handle null cases (no request body, no response body)

**Complexity mitigation**:
- Use Liquid filters for JSON escaping
- Create helper filters for common patterns
- Test with various endpoint shapes
- One-time effort, benefits all services

### Q6: What information do users want from companion endpoints?

**Resolution**: Four meta types cover all identified use cases.

| Meta Type | Primary Use Case |
|-----------|------------------|
| `endpoint-info` | Human discovery, documentation, deprecation checking |
| `request-schema` | Dynamic request construction, form generation |
| `response-schema` | Response parsing, type generation |
| `full-schema` | Complete documentation, code generation |

**User scenarios addressed**:
- **AI Agents**: Use full-schema to understand endpoints and construct requests
- **Debug Tools**: Use endpoint-info to display human-readable descriptions
- **Dynamic Clients**: Use request/response schemas to build UI without SDK
- **Documentation Generators**: Use full-schema to auto-generate docs

### Q7: NSwag Custom Filter Implementation

**Resolution**: Use OpenAPI extension data + template access via `ExtensionData`.

**Research Findings**:
- NSwag uses **Fluid.Core** (not DotLiquid) for Liquid template rendering
- Custom filters would require modifying NSwag library source code - **impractical**
- NSwag exposes `operation.ExtensionData["x-property-name"]` for accessing vendor extensions

**Recommended Implementation**:

1. **Add extension fields to OpenAPI specs** with pre-serialized JSON:
   ```yaml
   paths:
     /accounts/get:
       post:
         x-request-schema-json: |
           {"$schema":"http://json-schema.org/draft-07/schema#","type":"object",...}
         x-response-schema-json: |
           {"$schema":"http://json-schema.org/draft-07/schema#","type":"object",...}
         x-endpoint-info-json: |
           {"summary":"Get account by ID","tags":["accounts"],...}
   ```

2. **Access in Controller.liquid template**:
   ```liquid
   {% if operation.ExtensionData["x-request-schema-json"] %}
   private static readonly string _{{ operation.ActualOperationName }}_RequestSchema = """
   {{ operation.ExtensionData["x-request-schema-json"] }}
   """;
   {% endif %}
   ```

3. **Alternative**: Post-processing script that injects schema strings after NSwag generation

**Benefits**:
- Uses existing NSwag capabilities (no modifications needed)
- Schemas versioned alongside OpenAPI specs
- Generates compile-time constants

### Q8: Nested $ref Resolution Depth

**Resolution**: Bundle all dependencies into `$defs` section with internal references.

**Research Findings**:
- NSwag **inlines all `$ref` references** during code generation
- External file references (`$ref: 'other-file.yaml#/...'`) have limited support and often fail
- For embedded schemas, self-contained documents with `$defs` are the standard approach

**Recommended Strategy**:

1. **Convert external references to internal `$defs`**:
   ```json
   {
     "$schema": "http://json-schema.org/draft-07/schema#",
     "type": "object",
     "properties": {
       "status": { "$ref": "#/$defs/ServiceStatus" }
     },
     "$defs": {
       "ServiceStatus": {
         "type": "string",
         "enum": ["active", "inactive", "error"]
       }
     }
   }
   ```

2. **Handle recursive schemas**: Track visited schemas to prevent infinite loops during bundling

3. **Reference conversion**: Change `#/components/schemas/TypeName` → `#/$defs/TypeName` in serialized output

**Implementation Pattern**:
```csharp
// When serializing schema for embedding:
// 1. Collect all transitive $ref dependencies
// 2. Add each to $defs section
// 3. Replace #/components/schemas/ with #/$defs/ in all references
```

### Q9: Generated Companion Endpoints for Endpoints Without Bodies

**Resolution**: Return empty object `{}` as the schema.

**Rationale**:
- Empty JSON Schema `{}` is a valid schema meaning "any value is valid"
- Convention: `{}` signals "no body expected" to clients
- Consistent structure - always return a schema object, never null

**Implementation**:
```json
{
  "metaType": "request-schema",
  "endpointKey": "DELETE:/accounts/delete",
  "data": {}
}
```

**Client Handling**:
- Clients should interpret `{}` as "this endpoint does not accept/return a body"
- SDK documentation should clarify this convention
- Empty `{}` is preferable to `null` for type consistency

### Q10: Meta Flag Architecture (Clarified)

**Resolution**: Same GUID, Meta flag bit, Channel encodes meta type, path transformation, with shortcut API exception.

**Architecture Summary**:
- **Same GUID**: Use the identical GUID as the normal endpoint - no separate meta GUIDs
- **Meta flag (0x80)**: Set bit 7 in MessageFlags byte (alongside Response, Event, etc.)
- **Channel field encodes meta type**: 0=info, 1=request-schema, 2=response-schema, 3=full-schema
- **Empty payload**: Meta requests have EMPTY payload - Connect NEVER reads payloads (zero-copy)
- **Path transformation**: Connect appends `/meta/{type}` to original path based on Channel value
- **No permissions complexity**: If you can access the endpoint, you can access its meta

**Shortcut API Exception**:

Session shortcuts (pre-bound APIs handled by Connect itself) require special handling:

```csharp
// In Connect service meta request handling:
if (IsShortcutEndpoint(endpointKey))
{
    // Shortcut APIs are handled by Connect - don't route to backend
    // Request schema: Always {} (shortcuts don't accept client payloads)
    // Response schema: Must be provided in shortcut event definition

    var shortcutDef = GetShortcutDefinition(endpointKey);
    return metaType switch
    {
        MetaType.RequestSchema => BuildSchemaResponse(endpointKey, "{}"),
        MetaType.ResponseSchema => BuildSchemaResponse(endpointKey,
            shortcutDef.ResponseSchemaJson ?? "{}"),
        MetaType.EndpointInfo => BuildInfoResponse(endpointKey, shortcutDef.Info),
        MetaType.FullSchema => BuildFullSchemaResponse(endpointKey,
            shortcutDef.Info, "{}", shortcutDef.ResponseSchemaJson ?? "{}")
    };
}
else
{
    // Normal API - transform path and route to service
    var metaPath = $"{originalPath}/meta/{GetMetaTypeSuffix(metaType)}";
    return await RouteToServiceAsync(serviceName, metaPath, ...);
}
```

**Shortcut Event Schema Requirement**:
```yaml
# In shortcut event definition
x-shortcut-response-schema: |
  {"type":"object","properties":{"accountId":{"type":"string"},...}}
```

### Q11: Connect Service Meta Request Logging

**Resolution**: Log at Trace/Debug level, no special alerting.

**Implementation**:
```csharp
// In HandleMetaEndpointRequestAsync:
_logger.LogTrace("Meta request: {MetaType} for {EndpointKey}", metaType, endpointKey);

// On successful response:
_logger.LogDebug("Meta response sent: {MetaType} for {EndpointKey}", metaType, endpointKey);
```

**Rationale**:
- Meta requests are read-only introspection - low security concern
- May be frequent during development - shouldn't clutter production logs
- Trace/Debug levels allow filtering in production while enabling debugging when needed
- No alerts or special monitoring required

---

## Open Questions - Unresolved

*All previously identified questions have been resolved. New questions should be added here as they arise during implementation.*

---

## TENETS Compliance Checklist

This section verifies the implementation plan against the 19 development TENETS.

### Critical TENETS (Must Comply)

| Tenet | Requirement | Compliance | Notes |
|-------|-------------|------------|-------|
| **1. Schema-First Development** | OpenAPI schemas are source of truth | **COMPLIANT** | Static generation embeds OpenAPI directly |
| **2. Code Generation System** | 8-component pipeline | **COMPLIANT** | Extends NSwag template (component 1) |
| **6. Service Implementation Pattern** | Partial class, no controller edits | **COMPLIANT** | Companion endpoints generated in same partial |
| **10. Generated Code Integrity** | Never edit generated files | **COMPLIANT** | All meta endpoints are generated |
| **11. Single Source of Truth** | No duplication of definitions | **COMPLIANT** | Schema extracted once from OpenAPI |

### Supporting TENETS

| Tenet | Requirement | Compliance | Notes |
|-------|-------------|------------|-------|
| **3. Dapr Integration** | Use Dapr for state/events | N/A | Meta endpoints stateless |
| **4. Zero-Copy Routing** | Connect doesn't deserialize | **COMPLIANT** | Route transformation only |
| **5. WebSocket-First** | Binary protocol primary | **COMPLIANT** | Meta flag in binary protocol |
| **7. Plugin Architecture** | Services own their metadata | **COMPLIANT** | Each service has own companion endpoints |
| **8. Testing Strategy** | Unit, HTTP, Edge tests | **COMPLIANT** | All three tiers covered |
| **9. Format Standards** | LF line endings | **COMPLIANT** | Make format applied |

### Design Principles Alignment

| Principle | Alignment | Notes |
|-----------|-----------|-------|
| **Simplicity** | High | Static strings simpler than reflection |
| **Performance** | High | Zero runtime cost |
| **Maintainability** | High | Template change affects all services |
| **Testability** | High | Build-time schema verification possible |
| **Extensibility** | Medium | Custom meta types require template changes |

---

## References

- [WEBSOCKET-PROTOCOL.md](../WEBSOCKET-PROTOCOL.md) - Binary protocol specification
- [lib-connect/Protocol/](../../lib-connect/Protocol/) - Protocol implementation
- [JSON Schema Specification](https://json-schema.org/) - Schema format reference
- [OpenAPI 3.0 Specification](https://spec.openapis.org/oas/v3.0.3) - Source schema format
- [NSwag Documentation](https://github.com/RicoSuter/NSwag) - Code generation framework
- [Liquid Template Language](https://shopify.github.io/liquid/) - Template engine reference
- HTTP `HEAD` method - [RFC 7231 Section 4.3.2](https://datatracker.ietf.org/doc/html/rfc7231#section-4.3.2)
- [TENETS.md](../reference/TENETS.md) - Development principles and standards

---

## Implementation Checklist

### Phase 1: Protocol Layer
- [ ] Rename `Reserved` to `Meta` in `MessageFlags.cs`
- [ ] Add `IsMeta` property to `BinaryMessage.cs`
- [ ] Create `MetaType.cs` enum in `lib-connect/Protocol/` (values 0-3 matching Channel encoding)
- [ ] Document Channel field usage when Meta flag is set
- [ ] Update `BinaryProtocolTests.cs`
- [ ] Update WEBSOCKET-PROTOCOL.md documentation

### Phase 2: Connect Service Route Transformation
- [ ] Add meta flag detection in `RouteToServiceAsync()`
- [ ] Implement `GetMetaTypeSuffix(ushort channel)` method (reads Channel, not payload)
- [ ] Add path transformation: `{path}` → `{path}/meta/{type}`
- [ ] Handle shortcut API exception (Connect returns directly, no routing)
- [ ] Add shortcut response schema field to shortcut event definitions
- [ ] Add unit tests for route transformation logic

### Phase 3: Companion Controller Generation (Static)
- [ ] Add `x-request-schema-json`, `x-response-schema-json`, `x-endpoint-info-json` extension fields to OpenAPI schemas
- [ ] Create pre-processing script to populate extension fields with bundled JSON schemas
- [ ] Modify `Controller.liquid` template to access `operation.ExtensionData["x-*-json"]` fields
- [ ] Generate `static readonly string` fields with embedded schema JSON
- [ ] Generate companion endpoint methods per operation
- [ ] Handle endpoints with no request body (`{}` schema)
- [ ] Handle endpoints with no response body (`{}` schema)
- [ ] Convert `#/components/schemas/` refs to `#/$defs/` in embedded schemas
- [ ] Test generation with accounts-api.yaml as sample

### Phase 4: Response Models
- [ ] Create `bannou-service/Meta/MetaResponse.cs`
- [ ] Create `bannou-service/Meta/MetaResponseBuilder.cs`
- [ ] Implement `BuildInfoResponse()` method
- [ ] Implement `BuildSchemaResponse()` method
- [ ] Implement `BuildFullSchemaResponse()` method
- [ ] Add unit tests for MetaResponseBuilder

### Phase 5: SDK Client Support
- [ ] Add `MetaType` enum to SDK
- [ ] Add `GetEndpointMetaAsync<T>()` to `BannouClient.cs`
- [ ] Add convenience methods for each meta type
- [ ] Create `sdk-sources/MetaTypes.cs` with response models
- [ ] Update SDK tests

### Phase 6: Testing
- [ ] Create `MetaEndpointTestHandler.cs` in edge-tester
- [ ] Test all meta types via WebSocket with Meta flag
- [ ] Test direct HTTP access to companion endpoints
- [ ] Test error cases (unknown GUID, missing companion endpoint)
- [ ] Test schema fidelity (format, minLength, enum preserved)
- [ ] Validate returned JSON Schema against draft-07 specification

### Phase 7: Documentation & Standards
- [ ] Update `docs/reference/TENETS.md` to document meta endpoint extension data requirements
- [ ] Add new tenet or extend existing schema-first tenet to specify:
  - `x-request-schema-json`, `x-response-schema-json`, `x-endpoint-info-json` extensions
  - These extensions MUST be added to `{service}-api.yaml` files (where API schema data is kept)
  - NOT in event schemas, configuration schemas, or other schema types
- [ ] Document the pre-processing script in generation pipeline documentation
- [ ] Update `PLUGIN_DEVELOPMENT.md` with meta endpoint generation instructions

---

*This document is a living specification. Update as implementation progresses and questions are resolved.*
