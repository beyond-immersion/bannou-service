# CURRENT TASKS - Connect Service WebSocket Protocol Implementation

*Updated: 2025-01-19 - Implementation phase following successful authentication foundation*

## Executive Summary

**Implementation Phase**: We've successfully validated the architecture and are now implementing the designed systems according to technical guides. Code generation, JWT security model, and basic auth flows are working. Focus is on completing auth service implementation and Connect service WebSocket protocol per the Connect Service Implementation Guide.

**Key Insight**: We have a solid foundation but significant implementation work remains. This is about building out designed systems, not fixing broken architecture. Both http-tester (service-to-service) and edge-tester (client perspective) validation will be essential for ensuring complete functionality.

## ⚠️ CRITICAL BLOCKER - Segmentation Fault During HTTP Tests

**Issue**: Service crashes with exit code 139 (segmentation fault) during HTTP integration tests
**Impact**: Tests cannot pass, blocking all development progress
**Investigation Status**: Root cause unknown after extensive debugging

### Investigation Summary
1. **Confirmed**: Segfault occurs after processing several successful requests
2. **Confirmed**: Infrastructure and initial service startup work correctly
3. **Disproven**: HeaderArray filters are NOT the cause (disabled them, segfault persists)
4. **Unknown**: Actual root cause of the segmentation fault

### Potential Causes to Investigate
- .NET 9 runtime issue (preview version being used)
- Dapr integration memory management
- Service-to-service communication patterns
- Database/Redis connection issues
- Garbage collection conflicts
- Native library incompatibility
- Threading/async issues in request pipeline

### Next Investigation Steps
1. Add verbose logging around request completion pipeline
2. Check for memory leaks in service-to-service calls
3. Test with .NET 8 instead of .NET 9 preview
4. Isolate which specific service call triggers the crash
5. Run with debugging symbols and core dump analysis

## Current Implementation Status

### ✅ Foundation Architecture - Working Correctly

**Code Generation Pipeline**: ✅ **FULLY OPERATIONAL**
- Schema-first development working perfectly
- NSwag generation creates proper interfaces, controllers, models, clients
- Extension method authentication system operational
- Zero compilation errors across all services

**JWT Redis Security Model**: ✅ **CORRECTLY IMPLEMENTED**
- JWT tokens contain opaque `session_key` (not sensitive session data)
- Session data properly stored in Redis with TTL expiration
- ValidateTokenAsync ✅ IMPLEMENTED and working
- LogoutAsync ✅ IMPLEMENTED with proper session cleanup
- GetSessionsAsync ✅ IMPLEMENTED with JWT validation

**Service Communication**: ✅ **WORKING**
- Extension method header authentication (no casting required)
- Dapr service-to-service routing functional
- ServiceAppMappingResolver with "bannou" default working
- HTTP integration demonstrated (GetSessions test passing)

### 🚧 Auth Service - Partially Implemented

**Core Authentication**: 🚧 **FOUNDATION WORKING, DETAILS INCOMPLETE**
- ✅ Login/registration basic flow operational
- ✅ JWT generation and Redis session storage working
- ❌ OAuth provider integration (Discord, Google, etc.) incomplete
- ❌ Steam authentication incomplete
- ❌ Password reset functionality incomplete
- ❌ Multi-session management needs refinement

**Configuration Integration**: 🚧 **MIXED**
- ✅ AuthServiceConfiguration properly generated and used
- ❌ Some OAuth provider configurations incomplete
- ❌ Email service integration for password reset missing

### 🚧 Accounts Service - Event System Missing

**CRUD Operations**: ✅ **WORKING**
- Basic account creation, retrieval, update, delete functional
- Integration with auth service working

**Event Publishing**: ❌ **CRITICAL MISSING**
- Need account.created, account.updated, account.deleted events
- Auth service needs to subscribe to account.deleted → invalidate sessions
- Permission service integration for role changes

### ✅ Connect Service - What's Working

**HTTP API Endpoints**: ✅ **FULLY IMPLEMENTED**
- `ProxyInternalRequestAsync` - Service routing with permission validation
- `DiscoverAPIsAsync` - Dynamic API discovery with client-salted GUIDs
- `GetServiceMappingsAsync` - Service mapping monitoring
- ServiceAppMappingResolver integration for dynamic routing

**WebSocket Infrastructure**: ✅ **FOUNDATION COMPLETE**
- ConnectController.cs has WebSocket upgrade handling
- JWT validation integration with Auth service working
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

**RabbitMQ Integration**: ❌ **CRITICAL MISSING**
- Service-to-client RPC via RabbitMQ → Connect service → WebSocket routing
- Event broadcasting from services to connected clients
- Real-time capability updates when permissions change

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

### Phase 1: Complete Auth Service Implementation (CRITICAL - Week 1)

**Priority**: ✅ **COMPLETED** - All auth endpoints implemented and working

**✅ Completed Tasks**:
1. ✅ **ValidateTokenAsync**: Implemented with JWT extraction and Redis session lookup
2. ✅ **LogoutAsync**: Implemented with session cleanup from Redis
3. ✅ **GetSessionsAsync**: Implemented with JWT validation
4. ✅ **Configuration Integration**: AuthServiceConfiguration properly used
5. ✅ **Header-based authentication**: x-from-authorization extraction working
6. ✅ **Service client integration**: AuthClient methods exclude JWT parameters

### Phase 2: Fix Auth Service Integration Issues (HIGH PRIORITY - Week 1)

**Priority**: BLOCKING - Required for http-tester service-to-service validation

**Tasks**:
1. **Fix registration/login flow issues**: Debug http-tester failures
2. **Validate session management**: Login → validate → logout lifecycle testing
3. **Add session cleanup**: Expired session removal and monitoring
4. **Test service-to-service auth**: Verify all services can authenticate with Auth service
5. **OAuth provider integration**: Complete Discord, Google, Steam authentication flows

### Phase 3: Implement Accounts Service Events (HIGH PRIORITY - Week 1-2)

**Priority**: CRITICAL - Required for auth session invalidation

**Tasks**:
1. **Add event publishing to AccountsService**: account.created, account.updated, account.deleted
2. **Implement event subscription in AuthService**: Subscribe to account.deleted events
3. **Add session invalidation**: When account deleted → invalidate all sessions for that account
4. **Test event integration**: Account deletion → session cleanup verification
5. **Permission service integration**: Role changes trigger capability updates

### Phase 4: Complete Connect Service WebSocket Protocol (HIGH PRIORITY - Week 2)

**Priority**: Core functionality for real-time communication

**Tasks**:
1. **Un-obsolete WebSocket handling**: Remove obsolete markers and complete implementation
2. **Implement 31-byte binary protocol**: Message flags, channels, service GUIDs
3. **Complete binary message routing**: Route to Dapr services via ServiceAppMappingResolver
4. **Add Redis session integration**: WebSocket connection stickiness and heartbeats
5. **Implement bidirectional RPC**: Services → RabbitMQ → Connect → WebSocket client communication
6. **Test WebSocket flow**: JWT auth → connection → binary messages → service routing

### Phase 5: Update Testing Strategy (MEDIUM PRIORITY - Week 2)

**Priority**: Comprehensive validation before production deployment

**Tasks**:
1. **Fix HTTP Tests**: Registration → Login → API Discovery → Internal Proxy (service-to-service via Dapr, NOT OpenResty)
2. **Enhance Edge Tests**: Full WebSocket flow including binary protocol and service routing (client perspective through OpenResty)
3. **Add bidirectional RPC tests**: Service → RabbitMQ → Connect → WebSocket client flow
4. **Event integration tests**: Account events → auth session invalidation
5. **Session management tests**: Complete session lifecycle and cleanup validation

## Testing Strategy

### HTTP Tests (http-tester) - Service-to-Service Perspective

**Purpose**: Validate internal Dapr service communication (NOT client-facing)
**Network**: Internal Dapr service mesh (bypasses OpenResty entirely)
**Authentication**: Header-based with x-from-authorization extraction

✅ **Service-to-Service Communication**:
- Registration flow via AuthClient (service → auth service)
- Login flow via AuthClient (service → auth service)
- API discovery via ConnectClient (service → connect service)
- Internal proxy routing via ConnectClient (service → service via connect)
- Token validation between services (auth service validation)
- Account management via AccountsClient (service → accounts service)

❌ **NOT Client-Facing Endpoints**:
- HTTP tests should NOT go through OpenResty
- HTTP tests should NOT test WebSocket connections
- HTTP tests validate internal service contracts only

### Edge Tests (edge-tester) - Client Perspective

**Purpose**: Validate complete client experience through OpenResty
**Network**: Client → OpenResty → Connect service → Internal services
**Authentication**: JWT tokens in WebSocket upgrade and binary protocol

✅ **Client WebSocket Experience**:
- Complete registration → login → WebSocket connect flow (through OpenResty)
- JWT authentication for WebSocket upgrade
- 31-byte binary protocol message sending/receiving
- Service routing through WebSocket binary messages
- Real-time API discovery updates via WebSocket
- Session management and reconnection handling
- Bidirectional RPC: Service → RabbitMQ → Connect → WebSocket client

✅ **Client Event Reception**:
- Real-time capability updates when permissions change
- Service-initiated RPCs received via WebSocket
- Event broadcasting from services to connected clients

## Success Criteria

### Phase 1 Complete When:
- [x] ✅ Auth service ValidateTokenAsync implemented and working
- [x] ✅ Auth service LogoutAsync and GetSessionsAsync implemented
- [x] ✅ Header-based authentication (x-from-authorization) working
- [x] ✅ Service client integration excludes JWT parameters correctly
- [x] ✅ Connect service can validate JWT tokens for WebSocket authentication

### Phase 2 Complete When:
- [ ] Auth service registration/login flow issues resolved in http-tester
- [ ] Session management lifecycle fully tested (login → validate → logout)
- [ ] Service-to-service authentication working across all services
- [ ] OAuth provider integration (Discord, Google, Steam) complete

### Phase 3 Complete When:
- [ ] Accounts service publishes events (account.created, account.updated, account.deleted)
- [ ] Auth service subscribes to account.deleted events
- [ ] Session invalidation works when accounts are deleted
- [ ] Permission service integration triggers capability updates

### Phase 4 Complete When:
- [ ] WebSocket connections successfully authenticate via JWT
- [ ] Binary protocol (31-byte header) implemented and working
- [ ] Messages route from WebSocket clients to Dapr services
- [ ] Bidirectional RPC (service → RabbitMQ → Connect → client) working
- [ ] Redis session stickiness for WebSocket connections

### Phase 5 Complete When:
- [ ] HTTP tests validate service-to-service flows (bypassing OpenResty)
- [ ] Edge tests demonstrate complete client experience (through OpenResty)
- [ ] Bidirectional RPC testing via WebSocket protocol
- [ ] Event integration testing (account events → session invalidation)

### Production Ready When:
- [ ] All authentication flows tested and working in both test perspectives
- [ ] WebSocket binary protocol fully functional with bidirectional RPC
- [ ] Session management handles multi-device scenarios
- [ ] Event-driven session invalidation working reliably
- [ ] Performance meets requirements (1000+ concurrent WebSocket connections)

## Current Immediate Actions

### Week 1 Priorities (In Order)
1. ✅ **COMPLETED**: Auth service ValidateTokenAsync, LogoutAsync, GetSessionsAsync implementation
2. ✅ **COMPLETED**: Header-based authentication (x-from-authorization) integration
3. ✅ **COMPLETED**: Service client JWT parameter exclusion via NSwag filtering
4. **NEXT**: Fix auth service registration/login flow issues in http-tester
5. **NEXT**: Implement accounts service event publishing (account.created, account.updated, account.deleted)
6. **NEXT**: Add auth service event subscription for session invalidation

### Week 2 Priorities
1. **Complete Connect service WebSocket binary protocol** (31-byte header implementation)
2. **Implement bidirectional RPC** (Services → RabbitMQ → Connect → WebSocket client)
3. **Add Redis session stickiness for WebSocket connections**
4. **Update edge tests for complete WebSocket flow with bidirectional RPC**
5. **Test performance and connection limits**
6. **Enhance http-tester to validate service-to-service flows correctly**

## Bidirectional RPC Architecture

### Service-to-Client Communication Flow

**Purpose**: Enable services to initiate RPCs to connected WebSocket clients for real-time updates

**Technical Flow**: Service → RabbitMQ Event → Connect Service → WebSocket Client
1. **Service publishes event**: Any Bannou service publishes RPC event to RabbitMQ
2. **Connect service consumes**: Connect service subscribes to RPC events from RabbitMQ
3. **Client lookup**: Connect service maps event target to active WebSocket connection
4. **Binary protocol delivery**: Connect service forwards RPC as binary message to client
5. **Client response**: Client can respond via standard WebSocket binary protocol

**Event Types for Bidirectional RPC**:
- **Permission Updates**: Permissions service → client capability updates
- **Account Changes**: Accounts service → client profile/status updates
- **Auth Events**: Auth service → client session/security notifications
- **Real-time Notifications**: Any service → client real-time updates

**Implementation Requirements**:
```csharp
// Connect service RabbitMQ event handlers
[Topic("bannou-pubsub", "client-rpc-events")]
[HttpPost("handle-client-rpc")]
public async Task<IActionResult> HandleClientRpc([FromBody] ClientRpcEvent rpcEvent)
{
    // 1. Find WebSocket connection for target client/session
    // 2. Convert RPC event to binary protocol message
    // 3. Send via WebSocket to client
    // 4. Handle optional response routing back to originating service
}
```

**Testing Requirements**:
- **http-tester**: Validate service → RabbitMQ event publishing
- **edge-tester**: Validate complete service → RabbitMQ → Connect → client flow
- **Event integration**: Test account deletion → session invalidation → client notification

---

*This document reflects the current implementation state after completing Auth service foundation and header-based authentication. We are now in implementation land - the architecture is sound and we need to complete the remaining service integrations, event systems, and WebSocket protocol to enable the full autonomous NPC infrastructure.*
