# CURRENT TASKS - Auth/Connect Service Integration Implementation

*Updated: 2025-01-15 - Auth/Connect service analysis and implementation plan*

## Executive Summary

Following comprehensive analysis of Auth and Connect service schemas and implementations against the Connect Service Implementation Guide requirements, we have identified specific implementation gaps blocking WebSocket authentication. The architecture is sound but key auth validation and WebSocket protocol components are incomplete.

**Critical Discovery**: The Auth service has a `/auth/validate` endpoint defined in the schema with `x-controller-only: true`, but the actual service implementation is missing. This is the primary blocker for Connect service WebSocket authentication.

## Current Implementation Status

### ✅ Auth Service - What's Working

**JWT Redis Key Security Model**: ✅ **CORRECTLY IMPLEMENTED**
- JWT tokens contain opaque `session_key` instead of sensitive session data
- Session data properly stored in Redis with TTL expiration
- Refresh token rotation and storage implemented correctly
- Login/registration flows creating proper session structure

**Basic Authentication Flows**: ✅ **FUNCTIONAL**
- Account registration via AccountsClient integration working
- Login flow with password validation functional
- JWT token generation with session_key working
- Redis session storage operational

**Schema and Generation**: ✅ **COMPLETE**
- OpenAPI schema properly defines all endpoints
- NSwag generation creates proper interfaces and models
- Service registration and DI injection working correctly

### ❌ Auth Service - Critical Missing Implementation

**Token Validation Endpoint**: ❌ **BLOCKING ISSUE**
- Schema defines `/auth/validate` with `x-controller-only: true`
- AuthController.cs has placeholder implementation returning mock data
- **ValidateTokenAsync method completely missing from AuthService.cs**
- Connect service cannot validate JWT tokens for WebSocket authentication

**Session Management**: ❌ **INCOMPLETE STUBS**
- `TerminateSessionAsync` returns mock response, doesn't remove from Redis
- `GetSessionsAsync` returns hardcoded data, doesn't query Redis
- `LogoutAsync` doesn't invalidate session in Redis
- No actual session cleanup happening

**Configuration Usage**: ❌ **HARDCODED VALUES**
- AuthService.cs uses hardcoded JWT secrets and configuration
- `AuthServiceConfiguration` generated but not used in implementation
- Environment variables not properly loaded

### ✅ Connect Service - What's Working

**HTTP API Endpoints**: ✅ **FULLY IMPLEMENTED**
- `ProxyInternalRequestAsync` - Service routing with permission validation
- `DiscoverAPIsAsync` - Dynamic API discovery with client-salted GUIDs
- `GetServiceMappingsAsync` - Service mapping monitoring
- ServiceAppMappingResolver integration for dynamic routing

**WebSocket Infrastructure**: ✅ **PARTIALLY COMPLETE**
- ConnectController.cs has WebSocket upgrade handling
- JWT validation integration with Auth service (depends on ValidateTokenAsync)
- Connection management infrastructure in place
- Binary protocol classes and routing framework implemented

### ❌ Connect Service - Missing WebSocket Implementation

**Binary Protocol Handler**: ❌ **INCOMPLETE**
- 31-byte binary header parsing incomplete
- `HandleWebSocketCommunicationAsync` marked as obsolete
- Binary message routing to services incomplete
- Client-to-service RPC handling incomplete

**Redis Session Integration**: ❌ **INCOMPLETE**
- Session stickiness for WebSocket connections not implemented
- Redis-backed connection state management incomplete
- Session heartbeat and cleanup not implemented

**Service Routing**: ❌ **INCOMPLETE**
- Message routing to Dapr services via binary protocol incomplete
- Service GUID to WebSocket client mapping incomplete
- Bidirectional RPC (service-to-client calls) incomplete

## Registration → Login → Connect Flow Design

### Current Flow (What Works)
1. **Registration**: POST `/auth/register` → Creates account via AccountsClient → Returns JWT with session_key
2. **Login**: POST `/auth/login` → Validates credentials → Creates session in Redis → Returns JWT with session_key
3. **API Discovery**: POST `/connect/api-discovery` → Returns available APIs with client-salted GUIDs (requires JWT)
4. **Service Calls**: POST `/connect/internal/proxy` → Routes HTTP requests to services (requires JWT)

### Missing WebSocket Flow (Needs Implementation)
5. **WebSocket Connect**: GET `/connect/connect` with JWT → **BLOCKED**: ValidateTokenAsync missing
6. **Binary Protocol**: WebSocket communication with 31-byte headers → **INCOMPLETE**
7. **Service Routing**: Binary messages routed to services → **INCOMPLETE**

### Queue Service Integration (Future - Optional for Now)
The Connect Service Implementation Guide mentions queue service integration between login and connect:

**Enhanced Flow (With Queue Service)**:
1. Registration/Login (same as above)
2. **Queue Request**: POST `/queue/request-access` → Check capacity → Return queue position or grant token
3. **Connect with Queue**: GET `/connect/connect` with JWT + queue grant → Immediate connection
4. **Alternative**: Direct connect if capacity available (bypass queue)

**For Now**: Implement direct connect flow without queue service, but design to accommodate queue grants in AuthResponse/ConnectRequest when queue service is added later.

## Authentication Flow Requirements

### Auth Service ValidateTokenAsync Implementation

**CRITICAL MISSING**: This method must be implemented in AuthService.cs to unblock Connect service

```csharp
public async Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(CancellationToken cancellationToken = default)
{
    // 1. Extract Authorization header from HttpContext
    // 2. Parse "Bearer <jwt_token>" format
    // 3. Validate JWT signature with secret key
    // 4. Extract session_key from JWT claims
    // 5. Lookup session data from Redis using session_key
    // 6. Check session expiration
    // 7. Return session info (session_id, account_id, roles, etc.)
}
```

**Dependencies**:
- HttpContext access for Authorization header
- JWT validation with current secret
- Redis session lookup by session_key
- Session expiration checking

### Connect Service WebSocket Authentication

**Current Implementation** (ConnectController.cs):
```csharp
// This works but depends on Auth service ValidateTokenAsync
var sessionId = await connectService.ValidateJWTAndExtractSessionAsync(authorization, cancellationToken);
```

**ValidateJWTAndExtractSessionAsync** calls AuthClient.ValidateTokenAsync which currently returns mock data.

## Implementation Plan

### Phase 1: Implement Auth Service ValidateTokenAsync (CRITICAL - Week 1)

**Priority**: BLOCKING - Required for all WebSocket authentication

**Tasks**:
1. **Add ValidateTokenAsync to IAuthService interface**
2. **Implement ValidateTokenAsync in AuthService.cs**:
   - Extract JWT from HttpContext Authorization header
   - Validate JWT signature and expiration
   - Extract session_key from JWT claims
   - Lookup session data from Redis
   - Return proper ValidateTokenResponse
3. **Fix configuration usage**: Replace hardcoded values with AuthServiceConfiguration
4. **Test JWT validation flow**: Ensure Connect service can validate tokens

### Phase 2: Complete Session Management (HIGH PRIORITY - Week 1)

**Priority**: Required for production WebSocket connections

**Tasks**:
1. **Implement actual session termination**:
   - TerminateSessionAsync removes from Redis
   - LogoutAsync invalidates session properly
2. **Implement GetSessionsAsync**: Query actual session data from Redis
3. **Add session cleanup**: Expired session removal and monitoring
4. **Test session lifecycle**: Login → validate → logout → verify cleanup

### Phase 3: Complete Connect Service WebSocket Protocol (HIGH PRIORITY - Week 2)

**Priority**: Core functionality for real-time communication

**Tasks**:
1. **Un-obsolete WebSocket handling**: Remove obsolete markers and complete implementation
2. **Implement 31-byte binary protocol**: Message flags, channels, service GUIDs
3. **Complete binary message routing**: Route to Dapr services via ServiceAppMappingResolver
4. **Add Redis session integration**: WebSocket connection stickiness and heartbeats
5. **Test WebSocket flow**: JWT auth → connection → binary messages → service routing

### Phase 4: Update Testing (MEDIUM PRIORITY - Week 2)

**Tasks**:
1. **HTTP Tests**: Registration → Login → API Discovery → Internal Proxy (no WebSocket)
2. **Edge Tests**: Full WebSocket flow including binary protocol and service routing
3. **Auth validation tests**: Verify JWT validation works correctly
4. **Session management tests**: Test session lifecycle and cleanup

## Testing Strategy

### HTTP Tests Should Cover
✅ **Service-to-Service Communication** (no WebSocket):
- Registration flow via AuthClient
- Login flow via AuthClient
- API discovery via ConnectClient
- Internal proxy routing via ConnectClient
- Token validation between services

❌ **NOT WebSocket Connections**:
- HTTP tests should not establish WebSocket connections
- WebSocket testing is for edge-tester only

### Edge Tests Should Cover
✅ **Client WebSocket Experience**:
- Complete registration → login → WebSocket connect flow
- JWT authentication for WebSocket upgrade
- Binary protocol message sending/receiving
- Service routing through WebSocket binary messages
- Real-time API discovery updates
- Session management and reconnection

## Success Criteria

### Phase 1 Complete When:
- [ ] Auth service ValidateTokenAsync implemented and working
- [ ] Connect service can validate JWT tokens for WebSocket authentication
- [ ] Session management properly stores/retrieves from Redis
- [ ] HTTP tests pass for registration → login → API discovery flow

### Phase 2 Complete When:
- [ ] WebSocket connections successfully authenticate via JWT
- [ ] Binary protocol (31-byte header) implemented and working
- [ ] Messages route from WebSocket clients to Dapr services
- [ ] Edge tests demonstrate complete client experience

### Production Ready When:
- [ ] All authentication flows tested and working
- [ ] WebSocket binary protocol fully functional
- [ ] Session management handles multi-device scenarios
- [ ] Performance meets requirements (1000+ concurrent WebSocket connections)

## Current Immediate Actions

### Week 1 Priorities (In Order)
1. **Implement ValidateTokenAsync in AuthService.cs** (BLOCKING)
2. **Fix Auth service configuration usage** (hardcoded → environment)
3. **Implement session management methods** (terminate, cleanup)
4. **Update HTTP tests for registration/login flow**
5. **Test Auth service validation integration**

### Week 2 Priorities
1. **Complete Connect service WebSocket binary protocol**
2. **Implement Redis session stickiness for WebSocket**
3. **Update edge tests for complete WebSocket flow**
4. **Test performance and connection limits**
5. **Prepare for optional queue service integration**

---

*This document reflects the current state after comprehensive analysis of Auth and Connect service schemas and implementations. The architecture is sound - we need to complete the missing authentication validation and WebSocket protocol implementations to enable the full registration → login → WebSocket connect flow.*
