# Test Coverage Audit

*Generated: 2025-12-26*
*Updated: 2025-12-26 - HIGH PRIORITY items complete*

## Summary

This audit identifies service helper classes that were designed for DI-based unit testing but are currently missing tests.

## Coverage by Project

### lib-auth - GOOD COVERAGE ✅

| Service | Test File | Status |
|---------|-----------|--------|
| OAuthProviderService | OAuthProviderServiceTests.cs | ✅ Tested |
| SessionService | SessionServiceTests.cs | ✅ Tested |
| TokenService | TokenServiceTests.cs | ✅ Tested |

### lib-connect - GOOD COVERAGE ✅

| Service | Test File | Status |
|---------|-----------|--------|
| BannouSessionManager | BannouSessionManagerTests.cs | ✅ Tested (33 tests) |
| WebSocketConnectionManager | WebSocketConnectionManagerTests.cs | ✅ Tested (25 tests) |
| ClientEventQueueManager | ClientEventTests.cs | ⚠️ Partial |
| ClientEventRabbitMQSubscriber | ClientEventTests.cs | ⚠️ Partial |

**Status**: Core session management and WebSocket infrastructure now fully tested

### lib-orchestrator - MISSING TESTS ❌

| Service | Test File | Status |
|---------|-----------|--------|
| OrchestratorEventManager | - | ❌ **MISSING** |
| OrchestratorStateManager | - | ❌ **MISSING** |
| SmartRestartManager | - | ❌ **MISSING** |

**Priority**: MEDIUM - Deployment orchestration helpers

### lib-state - GOOD COVERAGE ✅

| Service | Test File | Status |
|---------|-----------|--------|
| InMemoryStateStore | InMemoryStateStoreTests.cs | ✅ Tested (37 tests) |
| StateStoreFactory | StateServiceTests.cs | ⚠️ Indirect |
| RedisDistributedLockProvider | RedisDistributedLockProviderTests.cs | ✅ Tested |
| MySqlStateStore | - | ⚠️ Integration only |
| RedisStateStore | - | ⚠️ Integration only |
| RedisSearchStateStore | - | ⚠️ Integration only |

**Status**: InMemoryStateStore fully tested - core state management verified

### lib-mesh - PARTIAL COVERAGE ⚠️

| Service | Test File | Status |
|---------|-----------|--------|
| MeshInvocationClient | - | ❌ **MISSING** |
| LocalMeshRedisManager | - | ❌ **MISSING** |
| MeshRedisManager | - | ⚠️ Integration only |

**Priority**: MEDIUM - Mesh routing infrastructure

### lib-messaging - GOOD COVERAGE ✅

| Service | Test File | Status |
|---------|-----------|--------|
| InMemoryMessageBus | InMemoryMessageBusTests.cs | ✅ Tested (30 tests) |
| NativeEventConsumerBackend | - | ❌ **MISSING** |
| MassTransitMessageBus | MessagingServiceTests.cs | ⚠️ Partial |
| MassTransitMessageSubscriber | - | ⚠️ Integration only |

**Status**: InMemoryMessageBus fully tested - core messaging infrastructure verified

### lib-voice - PARTIAL COVERAGE ⚠️

| Service | Test File | Status |
|---------|-----------|--------|
| ScaledTierCoordinator | ScaledTierCoordinatorTests.cs | ✅ Tested |
| P2PCoordinator | - | ❌ **MISSING** |
| SipEndpointRegistry | - | ❌ **MISSING** |
| VoiceRoomState | - | ❌ **MISSING** |

**Priority**: LOW - Voice features not yet production-critical

### lib-documentation - GOOD COVERAGE ✅

| Service | Test File | Status |
|---------|-----------|--------|
| SearchIndexService | SearchIndexServiceTests.cs | ✅ Tested |
| RedisSearchIndexService | - | ⚠️ Integration only |

## Priority Action Items

### HIGH PRIORITY (Core Infrastructure) - ✅ COMPLETE

1. ✅ **InMemoryStateStoreTests** - 37 tests (COMPLETED 2025-12-26)
2. ✅ **InMemoryMessageBusTests** - 30 tests (COMPLETED 2025-12-26)
3. ✅ **BannouSessionManagerTests** - 33 tests (COMPLETED 2025-12-26)
4. ✅ **WebSocketConnectionManagerTests** - 25 tests (COMPLETED 2025-12-26)

**Total new tests added: 125 tests**

### MEDIUM PRIORITY (Important Helpers)

5. **NativeEventConsumerBackendTests** - Event subscription routing
6. **SmartRestartManagerTests** - Deployment restart logic
7. **OrchestratorStateManagerTests** - Orchestrator state management
8. **MeshInvocationClientTests** - Service-to-service invocation

### LOW PRIORITY (Can Defer)

9. Voice service helpers (P2PCoordinator, SipEndpointRegistry, VoiceRoomState)
10. OrchestratorEventManager (event publishing wrapper)
11. LocalMeshRedisManager (local dev helper)

## Pre-existing Build Issues

~~The following test files have pre-existing build errors that need fixing:~~

- ~~`lib-messaging.tests/MessagingServiceTests.cs` - Missing `IHttpClientFactory` parameter in MessagingService constructor calls~~

**Status**: Fixed 2025-12-26 - MessagingServiceTests now builds and runs correctly

## Recommendations

1. ~~Fix pre-existing build errors first~~ ✅ DONE
2. ~~Start with HIGH priority items (InMemory* classes)~~ ✅ DONE (125 tests added)
3. Each test file should cover:
   - Constructor parameter validation
   - Core method functionality
   - Edge cases and error conditions
   - Thread safety where applicable (ConcurrentDictionary usage)
4. **Next**: Consider MEDIUM priority items when time permits
