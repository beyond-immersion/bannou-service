# TypeScript SDK and Unreal Engine Integration Planning

## Executive Summary

This document analyzes the feasibility and approach for:
1. **TypeScript SDK**: A code-generated TypeScript/JavaScript client SDK for Bannou services
2. **Unreal Engine Integration**: Helper artifacts to simplify Unreal C++/Blueprint consumption of Bannou services

## Current State Analysis

### Existing .NET SDK Architecture

The current SDK follows a **schema-driven code generation** approach:

```
schemas/*-api.yaml          → Python scripts + NSwag → Generated C# code
schemas/*-client-events.yaml → Python scripts       → Event registries
schemas/*-configuration.yaml → Python scripts       → Configuration classes
```

**Key Components:**
- `sdks/client/` - BannouClient with WebSocket protocol handling
- `sdks/client/Generated/Proxies/` - Typed service proxies (e.g., AuthProxy, CharacterProxy)
- `sdks/client/Generated/Events/` - ClientEventRegistry, endpoint metadata
- `sdks/core/` - Shared types (BannouJson, ApiException, base events)

**Generation Scripts:**
- `generate-client-proxies.py` - Generates typed proxy classes from OpenAPI schemas
- `generate-client-event-registry.py` - Generates event type ↔ name mappings
- `generate-client-endpoint-metadata.py` - Generates runtime type discovery

### WebSocket Binary Protocol

The protocol uses **hybrid binary headers + JSON payloads** (see `docs/WEBSOCKET-PROTOCOL.md`):

| Message Type | Header Size | Structure |
|--------------|-------------|-----------|
| Request | 31 bytes | Flags(1) + Channel(2) + Sequence(4) + ServiceGUID(16) + MessageID(8) + Payload |
| Response | 16 bytes | Flags(1) + Channel(2) + Sequence(4) + MessageID(8) + ResponseCode(1) + Payload |

**Key Protocol Features:**
- Client-salted GUIDs (SHA256-based, unique per session)
- 8 message flags (Binary, Encrypted, Compressed, HighPriority, Event, Client, Response, Meta)
- Channel multiplexing for fair scheduling
- Request/response correlation via 64-bit Message ID
- Meta endpoints for runtime schema introspection

---

## Part 1: TypeScript SDK

### Architecture Proposal

```
@beyondimmersion/bannou-core          # Shared types, JSON helpers
@beyondimmersion/bannou-client        # WebSocket client + typed proxies
@beyondimmersion/bannou-client-voice  # (optional) Voice chat extension
```

### Required Components

#### 1. Binary Protocol Implementation

```typescript
// Core binary message handling
class BinaryMessage {
  static readonly REQUEST_HEADER_SIZE = 31;
  static readonly RESPONSE_HEADER_SIZE = 16;

  static parseRequest(buffer: ArrayBuffer): RequestMessage;
  static parseResponse(buffer: ArrayBuffer): ResponseMessage;
  static serializeRequest(msg: RequestMessage): ArrayBuffer;
}

// Network byte order utilities
class NetworkByteOrder {
  static writeUInt16(view: DataView, offset: number, value: number): void;
  static readUInt16(view: DataView, offset: number): number;
  static writeUInt64(view: DataView, offset: number, value: bigint): void;
  static readUInt64(view: DataView, offset: number): bigint;
  static writeGuid(view: DataView, offset: number, guid: string): void;
  static readGuid(view: DataView, offset: number): string;
}
```

**Complexity:** HIGH - Requires careful TypedArray/DataView handling with big-endian byte order.

#### 2. Connection Manager

```typescript
class BannouClient {
  // Connection lifecycle
  connectAsync(email: string, password: string): Promise<void>;
  connectWithTokenAsync(jwt: string): Promise<void>;
  disconnectAsync(): Promise<void>;

  // RPC
  invokeAsync<TRequest, TResponse>(
    method: string, path: string, request: TRequest
  ): Promise<ApiResponse<TResponse>>;

  // Events
  sendEventAsync<TRequest>(method: string, path: string, request: TRequest): Promise<void>;
  onEvent<TEvent>(eventName: string, handler: (event: TEvent) => void): Disposable;

  // Capability manifest (received from server)
  private capabilities: Map<string, string>; // endpointKey → GUID
}
```

**Complexity:** HIGH - WebSocket lifecycle, reconnection, timeout handling.

#### 3. Request Correlation

```typescript
class PendingRequestMap {
  private pending: Map<bigint, {
    resolve: (response: any) => void;
    reject: (error: Error) => void;
    timer: number;
  }>;

  register(messageId: bigint, timeout: number): Promise<BinaryMessage>;
  resolve(messageId: bigint, response: BinaryMessage): void;
  reject(messageId: bigint, error: Error): void;
}
```

**Complexity:** MEDIUM - Promise-based correlation with timeout cleanup.

#### 4. Generated Typed Proxies

```typescript
// Generated from schemas/auth-api.yaml
export class AuthProxy {
  constructor(private client: IBannouClient) {}

  async loginAsync(request: LoginRequest): Promise<ApiResponse<LoginResponse>> {
    return this.client.invokeAsync('POST', '/auth/login', request);
  }

  async registerAsync(request: RegisterRequest): Promise<ApiResponse<RegisterResponse>> {
    return this.client.invokeAsync('POST', '/auth/register', request);
  }
}

// Extension on BannouClient
export interface BannouClient {
  readonly auth: AuthProxy;
  readonly account: AccountProxy;
  readonly character: CharacterProxy;
  // ... all 34 services
}
```

**Complexity:** LOW - Can mirror existing Python generation scripts.

#### 5. Generated Types (Request/Response Models)

```typescript
// Generated from schemas/auth-api.yaml components/schemas
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  accountId: string;
}
```

**Complexity:** LOW - Many tools exist (openapi-typescript, openapi-generator, etc.).

### Generation Tooling Options

| Tool | Pros | Cons |
|------|------|------|
| **openapi-typescript** | Pure types, minimal runtime | No client generation |
| **openapi-generator** | Full client + types | Heavy runtime, opinionated |
| **Custom Python scripts** | Full control, consistent with .NET | More maintenance |
| **NSwag (TypeScript mode)** | Already in use for .NET | Limited TS customization |

**Recommendation:** Hybrid approach:
1. Use `openapi-typescript` for type generation (minimal, correct)
2. Custom Python scripts for proxy/client generation (mirrors .NET pattern)
3. Hand-write binary protocol layer (too specialized for generators)

### TypeScript SDK Effort Estimate

| Component | Effort | Notes |
|-----------|--------|-------|
| Binary protocol | HIGH | Core infrastructure, must be correct |
| Connection manager | HIGH | Complex lifecycle, reconnection |
| Type generation | LOW | Use existing tools |
| Proxy generation | MEDIUM | Adapt Python scripts |
| Event registry | LOW | Adapt Python scripts |
| Testing | MEDIUM | Need browser + Node.js tests |
| Documentation | MEDIUM | API docs, getting started |

---

## Part 2: Unreal Engine Integration

### Challenge

Unreal Engine's networking is C++ based. Options for consuming Bannou services:

1. **HTTP-only mode** - Skip WebSocket, use REST endpoints (loses real-time)
2. **Full protocol implementation** - Reimplement binary protocol in C++
3. **Helper artifacts** - Generate schemas/types to assist manual implementation

### Option A: Client-Facing OpenAPI Schema

Generate a **simplified OpenAPI schema** containing only client-accessible endpoints:

```yaml
# Generated: bannou-client-api.yaml
openapi: 3.0.3
info:
  title: Bannou Client API
  description: Client-facing endpoints for game clients
  version: 2.0.0
servers:
  - url: wss://api.beyondimmersion.com
    description: WebSocket endpoint
paths:
  /auth/login:
    post:
      operationId: authLogin
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LoginRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LoginResponse'
components:
  schemas:
    LoginRequest:
      type: object
      required: [email, password]
      properties:
        email: { type: string }
        password: { type: string }
    LoginResponse:
      type: object
      properties:
        accessToken: { type: string }
        refreshToken: { type: string }
```

**Benefits:**
- Standard format, can use `openapi-generator` C++ codegen
- Swagger UI for documentation
- IDE support via JSON schemas

**Limitations:**
- OpenAPI C++ generators have varying quality
- Still need to implement WebSocket protocol manually

### Option B: Bannou-Specific YAML Format

Define a custom YAML schema optimized for client consumption:

```yaml
# bannou-client-schema.yaml
version: "2.0.0"
protocol:
  websocket:
    requestHeaderSize: 31
    responseHeaderSize: 16
    byteOrder: big-endian

services:
  auth:
    endpoints:
      login:
        method: POST
        path: /auth/login
        request: LoginRequest
        response: LoginResponse
      register:
        method: POST
        path: /auth/register
        request: RegisterRequest
        response: RegisterResponse

types:
  LoginRequest:
    email: { type: string, required: true }
    password: { type: string, required: true }

  LoginResponse:
    accessToken: { type: string }
    refreshToken: { type: string }
    expiresAt: { type: datetime }
    accountId: { type: uuid }

events:
  client:
    - name: game_session.player_joined
      type: PlayerJoinedEvent
    - name: game_session.player_left
      type: PlayerLeftEvent

enums:
  AuthProvider: [email, google, discord, twitch, steam]
```

**Benefits:**
- Designed for Bannou's specific needs
- Can include protocol details (byte order, header sizes)
- Simpler to parse than OpenAPI

**Limitations:**
- Need to build Unreal Editor importer
- Non-standard format

### Option C: C++ Header Generation

Generate header-only C++ with structs and endpoint constants:

```cpp
// Generated: BannouTypes.h
#pragma once

namespace Bannou {

// Request/Response structs
struct FLoginRequest {
    FString Email;
    FString Password;

    FString ToJson() const;
    static FLoginRequest FromJson(const FString& Json);
};

struct FLoginResponse {
    FString AccessToken;
    FString RefreshToken;
    FDateTime ExpiresAt;
    FGuid AccountId;

    static FLoginResponse FromJson(const FString& Json);
};

// Endpoint constants
namespace Endpoints {
    constexpr const char* AuthLogin = "POST:/auth/login";
    constexpr const char* AuthRegister = "POST:/auth/register";
    // ... all 406 endpoints
}

// Event names
namespace Events {
    constexpr const char* PlayerJoined = "game_session.player_joined";
    constexpr const char* PlayerLeft = "game_session.player_left";
}

} // namespace Bannou
```

**Benefits:**
- Direct C++ usage, no parsing
- UE4/UE5 compatible types (FString, FGuid, etc.)
- Compile-time type safety

**Limitations:**
- Large generated headers
- Need to regenerate on schema changes
- JSON serialization still manual

### Unreal Integration Recommendation

**Phased approach:**

1. **Phase 1:** Generate C++ headers + Client-facing OpenAPI schema
   - Provides immediate type safety
   - OpenAPI enables external tooling

2. **Phase 2:** Bannou-specific YAML + Unreal Editor plugin
   - Custom importer for better integration
   - Blueprint node generation

3. **Phase 3:** Full C++ WebSocket client (optional)
   - Complete protocol implementation
   - Needed for real-time features

---

## Implementation Plan

### Phase 1: Foundation (TypeScript)

1. Define package structure (`@beyondimmersion/bannou-*`)
2. Implement binary protocol layer
3. Implement connection manager
4. Set up type generation pipeline (openapi-typescript)
5. Create proxy generation scripts

### Phase 2: TypeScript Feature Parity

1. Event subscription infrastructure
2. Reconnection with session preservation
3. Meta endpoint support
4. Channel multiplexing

### Phase 3: Unreal Helpers

1. Generate client-facing OpenAPI schema
2. Generate C++ header files
3. Document manual WebSocket integration

### Phase 4: Unreal Plugin (Future)

1. Custom YAML format definition
2. Unreal Editor importer
3. Blueprint integration

---

## Open Questions

1. **TypeScript target environment:** Browser-only, Node.js-only, or universal?
2. **TypeScript bundling:** ESM, CJS, or both?
3. **Unreal Engine version:** UE4, UE5, or both?
4. **Feature scope:** Core API only, or include voice/assets?
5. **Timeline priority:** TypeScript SDK vs Unreal helpers?

---

## Files to Generate

### TypeScript SDK
- `scripts/generate-typescript-types.py` - Type generation orchestrator
- `scripts/generate-typescript-proxies.py` - Proxy class generator
- `scripts/generate-typescript-events.py` - Event registry generator
- `sdks/typescript/core/` - Core package
- `sdks/typescript/client/` - Client package

### Unreal Helpers
- `scripts/generate-client-schema.py` - Client-facing OpenAPI generator
- `scripts/generate-unreal-headers.py` - C++ header generator
- `schemas/Generated/bannou-client-api.yaml` - Client OpenAPI schema
- `unreal/BannouTypes.h` - Generated C++ headers

---

*Document created: 2025-01-20*
*Status: Planning*
