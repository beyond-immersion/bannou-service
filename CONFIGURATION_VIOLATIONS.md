# Configuration Violations (T21)

> **Last Updated**: 2026-01-24
> **Purpose**: Track configuration properties that exist but aren't used, and hardcoded values that should be configurable.

---

## 1. Unused Configuration Properties

Properties defined in configuration schemas but never referenced in service code.

### lib-actor (4 unused)

| Property | Notes |
|----------|-------|
| `MessageQueueSize` | Actor message queue capacity — not enforced |
| `ControlPlaneAppId` | Distributed orchestration app ID — not referenced |
| `InstanceStatestoreName` | State store routing — not referenced |
| `StateUpdateTransport` | Event transport selection — not referenced |

### lib-asset (1 unused)

| Property | Notes |
|----------|-------|
| `ProcessingJobPollIntervalSeconds` | Job polling interval — not referenced |

### lib-connect (3 unused)

| Property | Notes |
|----------|-------|
| `ReconnectionWindowExtensionMinutes` | Reconnection grace period — not referenced |
| `ConnectionTimeoutSeconds` | Client connection timeout — not referenced |
| `WebSocketKeepAliveIntervalSeconds` | WebSocket keep-alive — not referenced |

### lib-mapping (1 unused)

| Property | Notes |
|----------|-------|
| `AuthorityHeartbeatIntervalSeconds` | Map authority heartbeat interval — not referenced |

### lib-messaging (5 unused)

| Property | Notes |
|----------|-------|
| `ConnectionRetryCount` | RabbitMQ connection retries — not referenced |
| `ConnectionRetryDelayMs` | RabbitMQ retry delay — not referenced |
| `ConnectionTimeoutSeconds` | RabbitMQ connection timeout — not referenced |
| `RabbitMQNetworkRecoveryIntervalSeconds` | Network recovery interval — not referenced |
| `RequestTimeoutSeconds` | Request timeout — not referenced |

### lib-save-load (1 unused)

| Property | Notes |
|----------|-------|
| `ThumbnailUrlTtlMinutes` | Pre-signed thumbnail URL TTL — not referenced |

### lib-scene (4 unused)

| Property | Notes |
|----------|-------|
| `CheckoutExpirationCheckIntervalSeconds` | Checkout expiration check interval — not referenced |
| `CheckoutHeartbeatIntervalSeconds` | Checkout heartbeat interval — not referenced |
| `DefaultVersionRetentionCount` | Version history pruning limit — not referenced |
| `MaxSceneSizeBytes` | Scene size validation — not referenced |

### lib-state (1 unused)

| Property | Notes |
|----------|-------|
| `ConnectRetryCount` | Redis connection retries — not referenced |

### lib-voice (1 unused)

| Property | Notes |
|----------|-------|
| `KamailioRequestTimeoutSeconds` | SIP request timeout — not referenced |

### lib-character-encounter (1 unused)

| Property | Notes |
|----------|-------|
| `MemoryRefreshBoost` | Memory refresh strength multiplier — not referenced |

### lib-character (1 unused)

| Property | Notes |
|----------|-------|
| `CharacterListUpdateMaxRetries` | Retry count for list updates — not referenced |

**Total: 23 unused configuration properties across 11 services**

---

## 2. Hardcoded Tunables

Values hardcoded in service code that should come from configuration.

### Retry Counts (4 instances)

| File | Method | Value |
|------|--------|-------|
| lib-asset/AssetService.cs | `AddToIndexWithOptimisticConcurrencyAsync` | `maxRetries = 5` |
| lib-asset/AssetService.cs | `RemoveFromIndexWithOptimisticConcurrencyAsync` | `maxRetries = 5` |
| lib-game-session/GameSessionService.cs | `StoreSubscriberSessionAsync` | `attempt < 3` |
| lib-game-session/GameSessionService.cs | `RemoveSubscriberSessionAsync` | `attempt < 3` |

### Background Service Startup Delays (7 instances)

| File | Value | Config Property Exists? |
|------|-------|------------------------|
| lib-achievement/RarityCalculationService.cs | `TimeSpan.FromSeconds(30)` | YES — not wired |
| lib-asset/AssetService.cs (x2) | `TimeSpan.FromMilliseconds(10 * (attempt + 1))` | NO |
| lib-currency/Services/CurrencyAutogainTaskService.cs | `TimeSpan.FromSeconds(15)` | YES — not wired |
| lib-documentation/Services/SearchIndexRebuildService.cs | `TimeSpan.FromSeconds(5)` | NO |
| lib-mesh/Services/MeshHealthCheckService.cs | `TimeSpan.FromSeconds(10)` | YES — not wired |
| lib-actor/Pool/PoolHealthMonitor.cs | `TimeSpan.FromSeconds(5)` | YES — not wired |

### Static TTL Fields (4 instances)

| File | Value | Purpose |
|------|-------|---------|
| lib-orchestrator/OrchestratorStateManager.cs | `TimeSpan.FromSeconds(90)` | Heartbeat TTL |
| lib-orchestrator/OrchestratorStateManager.cs | `TimeSpan.FromMinutes(5)` | Routing TTL |
| lib-orchestrator/OrchestratorStateManager.cs | `TimeSpan.FromDays(30)` | Config history TTL |
| lib-contract/ContractServiceClauseValidation.cs | `TimeSpan.FromSeconds(15)` | Cache staleness threshold |

**Total: 15 hardcoded tunables across 7 services**

---
