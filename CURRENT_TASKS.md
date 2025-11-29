# COMPREHENSIVE ARCHITECTURAL PLAN: Permissions & Connect Service Revolution

*Updated: 2025-01-21 - Post-logging cleanup architectural analysis with comprehensive overhaul plan*

## 🎯 COMPLETE ARCHITECTURAL OVERHAUL PLAN

This document replaces the current implementation tracking with a comprehensive architectural plan that addresses fundamental misunderstandings in the current system design. We are moving from patching incomplete code to implementing proper, production-ready services with clean separation of concerns.

## 🚫 CRITICAL ARCHITECTURAL VIOLATIONS TO FIX

### 1. Connect Service Architectural Violations
The Connect service currently has **COMPLETELY WRONG** responsibilities:
- ❌ **API Discovery endpoint** (`/api-discovery`) - WRONG! This belongs to Permissions service
- ❌ **Permission mapping storage** - WRONG! Connect only CONSUMES permissions via events
- ❌ **Service registration handling** - WRONG! Connect doesn't register services

**Connect Service's ONLY Jobs:**
1. Handle WebSocket connections from clients
2. Parse and route binary protocol messages
3. Subscribe to permission updates via RabbitMQ channels
4. Validate messages against cached permissions
5. Forward messages to appropriate services via Dapr

### 2. Permissions Service Missing Implementation
The Permissions service is COMPLETELY MISSING but should be the **authoritative source** for:
- All role → state → endpoint permission mappings
- Compiling session capabilities based on current states
- Publishing capability updates to Connect services
- Managing service API registrations

### 3. Event Architecture Confusion
Current system confuses three COMPLETELY DIFFERENT concepts:
- **Service Mappings** (`service → Dapr app_id`) - for routing, used by ALL services
- **Permission Mappings** (`role → state → endpoints`) - ONLY in Permissions service
- **Session Capabilities** - compiled permissions sent to specific Connect instances

## 📐 CORRECT ARCHITECTURAL DESIGN

### Service Responsibility Matrix

| Service | Responsibilities | What It DOESN'T Do |
|---------|-----------------|-------------------|
| **Connect** | • WebSocket connections<br>• Binary protocol parsing<br>• Message routing via Dapr<br>• Cache permissions from events<br>• Validate against cached permissions | • Store permission mappings<br>• Compile capabilities<br>• Handle service registrations<br>• Provide API discovery |
| **Permissions** | • Store all permission matrices<br>• Compile session capabilities<br>• Publish updates to Connect<br>• Handle service registrations<br>• Manage Redis permission data | • Handle WebSocket connections<br>• Route messages<br>• Authenticate users<br>• Create accounts |
| **Auth** | • JWT generation/validation<br>• Session management in Redis<br>• Login/logout flows<br>• OAuth integration | • Store permissions<br>• Compile capabilities<br>• Handle WebSocket |
| **Accounts** | • CRUD operations<br>• Publish lifecycle events<br>• Store account data | • Authenticate<br>• Manage permissions<br>• Handle sessions |

### Event Flow Architecture

#### Authentication → Capability Compilation Flow
```
1. Client → Auth Service: Login request
2. Auth Service → Redis: Create session with roles
3. Auth Service → RabbitMQ: Publish "auth.session.created" event
4. Permissions Service → Subscribe: Receive auth event
5. Permissions Service → Redis: Lookup role permissions
6. Permissions Service → Redis: Compile and store session capabilities
7. Permissions Service → RabbitMQ: Publish to "CONNECT_{session_id}" channel
8. Connect Service → Subscribe: Receive capability update (specific instance)
9. Connect Service → Cache: Store permissions locally
10. Connect Service → WebSocket: Send capability update to client
```

#### Service Registration Flow
```
1. Any Service → Startup: Extract x-permissions from OpenAPI schema
2. Service → RabbitMQ: Publish ServiceRegistrationEvent
3. Permissions Service → Subscribe: Receive registration
4. Permissions Service → Redis: Update permission matrix atomically
5. Permissions Service → Redis: Get all active sessions
6. Permissions Service → Loop: Recompile each session's capabilities
7. Permissions Service → RabbitMQ: Publish updates to each CONNECT_{session_id}
```

### Redis Data Architecture (Permissions Service)

```redis
# Active sessions tracking
SADD active_sessions "session_123" "session_456"
EXPIRE active_sessions:{session_id} 3600

# Session states (service → state mapping)
HSET session:{session_id}:states
  "auth" "authenticated"
  "game-session" "in_lobby"
  "character" "character_selected"

# Compiled permissions (cached result with version)
HSET session:{session_id}:permissions
  "accounts" '["GET:/accounts", "PUT:/accounts/{id}"]'
  "character" '["GET:/character", "POST:/character/actions"]'
  "version" "42"

# Permission matrices (role → state → endpoints)
SADD permissions:auth:authenticated:user
  "GET:/accounts"
  "PUT:/accounts/{id}"

SADD permissions:game-session:in_lobby:user
  "POST:/game/ready"
  "GET:/game/players"
```

### Dynamic Channel Subscriptions (Connect Service)

```csharp
// NOT using [Topic] attribute - manual runtime subscription
public async Task OnWebSocketConnected(string sessionId, WebSocket socket)
{
    // Subscribe to session-specific channels at runtime
    var connectChannel = $"CONNECT_{sessionId}";
    var clientChannel = $"CLIENT_{sessionId}";

    // Manual RabbitMQ subscription
    var subscription = await _rabbitMq.SubscribeAsync(connectChannel,
        async (PermissionCapabilityUpdate update) =>
        {
            await HandlePermissionUpdate(sessionId, update);
        });
}
```

## 🔧 IMPLEMENTATION PHASES

### Phase 1: Schema Corrections
1. **Fix connect-api.yaml**
   - Remove `/api-discovery` endpoint completely
   - Remove `ApiDiscoveryResponse`, `ApiEndpointInfo` schemas
   - Keep only `/internal/proxy` and `/connect` endpoints

2. **Create permissions-events.yaml**
   ```yaml
   ServiceRegistrationEvent:  # Services → Permissions
   PermissionCapabilityUpdate: # Permissions → Connect instances
   SessionStateChangeEvent:    # Services → Permissions
   ```

3. **Update permissions-api.yaml**
   - Already has correct endpoints
   - Document Redis structure requirements
   - Add event publishing specifications

### Phase 2: Permissions Service Implementation
1. **Create lib-permissions service from scratch**
2. **Implement Redis data structures with atomic operations**
3. **Service registration handler**
4. **Session state management**
5. **Capability compilation engine**
6. **RabbitMQ event publishing**

### Phase 3: Connect Service Fixes
1. **Remove DiscoverAPIsAsync method completely**
2. **Implement manual RabbitMQ subscriptions**
3. **Complete WebSocket binary protocol handler**
4. **Fix message validation against cached permissions**

### Phase 4: Service Registration Pattern
1. **Add x-permissions to all service schemas**
2. **Implement startup registration in each service**
3. **Test registration → compilation → update flow**

## 🚨 FORBIDDEN PRACTICES (NO MORE!)

### No More Incomplete Code
- ❌ **NO TODO comments** in implementation
- ❌ **NO mock data returns**
- ❌ **NO obsolete method markers**
- ❌ **NO placeholder implementations**
- ✅ **ONLY complete, working code**

### No More Architectural Violations
- ❌ **NO permissions logic in Connect service**
- ❌ **NO API discovery in Connect service**
- ❌ **NO direct permission storage in Connect**
- ✅ **ONLY proper separation of concerns**

### No More Event Confusion
- ❌ **NO mixing service mappings with permissions**
- ❌ **NO broadcast to all Connect instances**
- ✅ **ONLY targeted events to specific instances**

## 📊 RACE CONDITION PREVENTION

### Atomic Redis Operations
```lua
-- Lua script for atomic permission updates
local session_id = KEYS[1]
local service_id = ARGV[1]
local new_state = ARGV[2]

redis.call('HSET', 'session:'..session_id..':states', service_id, new_state)
local version = redis.call('HINCRBY', 'session:'..session_id..':permissions', 'version', 1)
return version
```

### Distributed Locking
```csharp
var lockKey = $"lock:service-registration:{serviceId}";
using var redLock = await _redis.AcquireLockAsync(lockKey, TimeSpan.FromSeconds(10));
```

## ✅ SUCCESS CRITERIA

### Architecture Success
- ✅ Connect service has NO API discovery endpoints
- ✅ Permissions service owns ALL permission logic
- ✅ Clean separation of concerns across all services
- ✅ No confusion between routing and permissions

### Implementation Success
- ✅ All services auto-register APIs on startup
- ✅ Real-time capability updates via targeted RabbitMQ channels
- ✅ O(1) permission validation using Redis
- ✅ No race conditions in permission compilation
- ✅ Zero incomplete or mock implementations

### Event Architecture Success
- ✅ Service mappings separate from permission mappings
- ✅ Session-specific channels for targeted updates
- ✅ Delta updates minimize bandwidth usage
- ✅ Version tracking prevents missed updates

## 🎯 IMMEDIATE ACTIONS

1. ✅ **Update CURRENT_TASKS.md with this comprehensive plan**
2. 🔄 **Fix Connect service schema - remove wrong endpoints**
3. **Create permissions-events.yaml with correct event schemas**
4. **Implement Permissions service from scratch**
5. **Fix Connect service - remove discovery, add subscriptions**
6. **Implement service registration pattern**
7. **Test complete flow end-to-end**

## 🔄 NO MORE PATCHES - ONLY PROPER IMPLEMENTATIONS

From this point forward:
- Every implementation is complete or not started
- Every service follows proper architectural boundaries
- Every event has clear producer/consumer relationships
- Every race condition is prevented with atomic operations
- Every permission decision goes through Permissions service

This is not iterative development - this is FIXING FUNDAMENTAL ARCHITECTURAL MISTAKES and implementing the system CORRECTLY from the ground up.

## 📋 CURRENT IMPLEMENTATION STATUS

### ✅ Recently Completed (This Session)
1. **Logging Standards Cleanup**: ✅ COMPLETE
   - Removed all emoji characters from logging statements across entire codebase
   - Fixed 93+ syntax errors created during bulk logging cleanup
   - Standardized log levels (Debug, Info, Warning, Error)
   - Updated plugin generator templates to prevent future emoji generation
   - Professional logging standards now applied universally

2. **Service Architecture Foundation**: ✅ STABLE
   - Auth service ValidateTokenAsync, LogoutAsync, GetSessionsAsync implemented and working
   - Accounts service CRUD operations functional
   - Service generation pipeline operational with zero compilation errors
   - Basic Dapr service communication working with "bannou" routing

3. **File Reorganization**: ✅ COMPLETE
   - Moved service mapping classes from ServiceClients/ to Services/ directory
   - Deleted obsolete controller and attribute files
   - Created new schema files (common-events.yaml, permissions-events.yaml)
   - Added permission extraction scripts

### ❌ Critical Architectural Issues Identified
1. **Connect Service Violations**: Has wrong API discovery endpoint and permission storage responsibilities
2. **Permissions Service Gap**: Implementation exists but lacks proper Redis data structures and event handling
3. **Event Architecture Confusion**: Mixing service mappings (routing) with permission mappings (authorization)
4. **Missing Service Registration**: No automatic API registration from OpenAPI x-permissions sections

### 🔧 Current Development Phase
**ARCHITECTURAL CORRECTION PHASE** - Post-cleanup analysis has revealed fundamental design violations requiring systematic correction. Moving from piecemeal patches to complete, architecturally correct implementations.

### 🎯 Immediate Next Steps (Priority Order)
1. **Fix Connect Service Schema**: Remove `/api-discovery` endpoint and related schemas
2. **Complete Permissions Service**: Implement Redis data structures and capability compilation engine
3. **Implement Event System**: Create proper event flows for service registration and permission updates
4. **Add Service Registration**: Auto-extract and register APIs from OpenAPI x-permissions sections
