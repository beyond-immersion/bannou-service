# CURRENT TASKS - Bannou Service Architecture Implementation

*Created: 2025-01-15 - Comprehensive task analysis following architecture review*

## Executive Summary

After thorough review of the Connect Service Implementation Guide, Queue Service Implementation Guide, and API-DESIGN documentation, the current Bannou architecture is **fundamentally sound**. The issues we're encountering are not architectural problems but rather **incomplete implementations** of correctly designed systems.

## Current State Analysis

### ✅ What's Working Correctly

**Schema-Driven Service Generation**:
- All 6 core services (Accounts, Auth, Behavior, Connect, Permissions, Website) build successfully
- NSwag generation pipeline creates proper controllers, models, and clients
- Service registration and dependency injection working correctly

**Service Architecture**:
- ServiceAppMappingResolver correctly defaults to "bannou" for local development
- Generated clients properly use resolver for dynamic routing
- Dapr integration patterns established and working

**HTTP Integration Testing**:
- Basic service endpoints functional (Accounts CRUD operations pass)
- Service-to-service communication via Dapr working
- Authentication and authorization foundations in place

### ⚠️ What's Incomplete (Current Problems)

**Connect Service - WebSocket Protocol** (50% Complete):
- ✅ HTTP APIs implemented (ProxyInternalRequestAsync, DiscoverAPIsAsync)
- ❌ WebSocket binary protocol incomplete (31-byte header system)
- ❌ Redis session management not integrated
- ❌ Binary message routing pipeline incomplete

**Service Mapping Architecture** (80% Complete):
- ✅ ServiceAppMappingResolver implemented correctly
- ✅ "bannou" default routing working
- ❌ RabbitMQ event handling for dynamic mappings incomplete
- ❌ Service Coordinator not yet implemented

**Infrastructure Testing** (Misaligned):
- ✅ Basic infrastructure tests working
- ❌ Service mapping tests incorrectly trying to test Connect service endpoints
- ❌ Tests should be testing ServiceAppMappingResolver behavior, not HTTP endpoints

## Architectural Clarifications

### Connect Service Role (CORRECT UNDERSTANDING)
**Primary Function**: WebSocket-first edge gateway for client connections
- Handles WebSocket connection establishment and JWT authentication
- Routes binary messages between clients and services using service GUIDs
- Manages real-time capability updates via Permissions service events
- **NOT responsible for**: Service-to-app-id mappings or service discovery

### Service Mapping Architecture (CORRECT UNDERSTANDING)
**App-ID Mappings**: Managed by future Service Coordinator via RabbitMQ events
- All services use ServiceAppMappingResolver to determine routing
- Default: Everything routes to "bannou" (local development)
- Production: Dynamic updates via `bannou-service-mappings` RabbitMQ topic
- **NOT managed by**: Connect service or individual services

**API Mappings**: Managed by Permissions service
- Compiles session capabilities based on roles and service permissions
- Publishes updates to Connect service via `bannou-session-capabilities` topic
- Connect service uses these for client GUID generation and access control
- **Different from**: Service-to-app-id mappings

### Queue Service Role (NOT YET IMPLEMENTED)
**Primary Function**: Centralized queue management with capacity reporting
- Dual endpoint pattern: external (queue management) + internal (capacity reporting)
- Reports queue capacity to Connect service for load balancing
- Manages persistent game session queues
- **Integration with**: Connect service for capacity-based routing decisions

## Required Fixes

### 1. Fix Service Mapping Tests (IMMEDIATE - HIGH PRIORITY)

**Problem**: Tests incorrectly assume Connect service manages app-id mappings
**Solution**: Test ServiceAppMappingResolver behavior directly

**Tasks**:
- [ ] Rewrite DaprServiceMappingTestHandler to test ServiceAppMappingResolver
- [ ] Remove HTTP endpoint testing from infrastructure tests
- [ ] Add unit tests for mapping updates, removal, and default behavior
- [ ] Test RabbitMQ event simulation (mock event publishing)

### 2. Complete Connect Service WebSocket Protocol (HIGH PRIORITY)

**Current Status**: HTTP APIs complete, WebSocket incomplete
**Remaining Work**: Binary protocol implementation

**Tasks**:
- [ ] Implement 31-byte binary header parsing (MessageFlags, Channel, Sequence, Service GUID, Message ID)
- [ ] Complete WebSocket connection handler with JWT authentication
- [ ] Integrate Redis session management (replace in-memory storage)
- [ ] Implement binary message routing to services via ServiceAppMappingResolver
- [ ] Add RabbitMQ event handlers for real-time capability updates

### 3. Remove Incorrect BaseURL Logic (MEDIUM PRIORITY)

**Problem**: Services shouldn't determine their own base URLs
**Solution**: Remove baseUrl logic, rely entirely on ServiceAppMappingResolver

**Tasks**:
- [ ] Remove baseUrl properties from OpenAPI schemas
- [ ] Remove baseUrl logic from DaprServiceClientBase
- [ ] Ensure all routing goes through ServiceAppMappingResolver.GetAppIdForService()
- [ ] Update generated clients to use resolver-based routing only

### 4. Implement Service Coordinator (FUTURE - LOW PRIORITY)

**Purpose**: Publish service-to-app-id mapping events for distributed deployment
**Timeline**: After Connect service WebSocket protocol complete

**Tasks**:
- [ ] Create `lib-coordinator` service plugin
- [ ] Implement RabbitMQ event publishing for service mappings
- [ ] Add service discovery and topology management
- [ ] Integrate with ServiceAppMappingResolver event handling

### 5. Implement Queue Service (FUTURE - MEDIUM PRIORITY)

**Purpose**: Centralized queue management with Connect service integration
**Dependencies**: Connect service WebSocket protocol complete

**Tasks**:
- [ ] Create `lib-queue` service plugin following documented architecture
- [ ] Implement dual endpoint pattern (external + internal)
- [ ] Add capacity reporting to Connect service
- [ ] Integrate with game session management

## Testing Strategy Corrections

### Infrastructure Tests Should Test:
✅ **ServiceAppMappingResolver Behavior**:
- Default "bannou" routing
- Dynamic mapping updates via RabbitMQ events (mocked)
- Mapping removal and fallback behavior
- Thread-safe concurrent access

❌ **NOT Connect Service HTTP Endpoints**:
- Service mapping tests should not make HTTP calls
- Infrastructure tests are for component testing, not integration testing
- HTTP endpoints are tested in integration tests, not infrastructure tests

### HTTP Integration Tests Should Test:
✅ **Service-to-Service Communication**:
- Generated clients using ServiceAppMappingResolver correctly
- Actual service endpoints responding properly
- Authentication and authorization flows

## Implementation Priority Order

### Phase 1: Fix Current Issues (Week 1)
1. **Fix service mapping tests** - Remove incorrect HTTP endpoint testing
2. **Complete Connect service WebSocket protocol** - Binary message handling
3. **Remove baseUrl logic** - Simplify to resolver-only routing

### Phase 2: Complete Core Infrastructure (Week 2-3)
1. **Integrate Redis session management** - Replace in-memory storage
2. **Add RabbitMQ event handlers** - Real-time capability updates
3. **Implement Service Coordinator** - Dynamic mapping events

### Phase 3: Add Advanced Features (Week 4+)
1. **Implement Queue Service** - Capacity reporting integration
2. **Production deployment patterns** - Multi-instance scaling
3. **Advanced testing scenarios** - Distributed deployment simulation

## Success Criteria

### Phase 1 Complete When:
- [ ] All service mapping tests pass without HTTP calls
- [ ] Connect service handles WebSocket binary protocol correctly
- [ ] All routing goes through ServiceAppMappingResolver (no baseUrl logic)
- [ ] HTTP integration tests demonstrate proper service communication

### Architecture Validated When:
- [ ] Single "bannou" instance handles all services locally
- [ ] Dynamic mapping updates work via RabbitMQ events (mocked)
- [ ] Client connections receive real-time capability updates
- [ ] Service-to-service calls route correctly through resolver

## Notes

**Architecture Confidence**: The current design is sound and follows best practices
**Implementation Gap**: We're implementing correctly designed systems, not fixing broken architecture
**Testing Alignment**: Tests need to match the actual responsibilities of each service
**Documentation Quality**: Implementation guides provide clear, accurate architectural direction

---

*This document should be updated as tasks are completed and new issues are discovered.*
