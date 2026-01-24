# Missing Implementation Features

> **Purpose**: Configuration properties that represent genuinely useful features we intended to have but never implemented.
> **Source**: Analysis of unwired configuration properties across all services.
> **Last Updated**: 2026-01-24

---

## Completed Features

All items below have been implemented and verified with a clean build (0 warnings, 0 errors).

### lib-mesh: Production Resilience Features (ALL COMPLETE)

| Feature | Properties | Implementation |
|---------|-----------|----------------|
| Circuit Breaker | `CircuitBreakerEnabled`, `CircuitBreakerThreshold`, `CircuitBreakerResetSeconds` | `MeshStateManager` tracks consecutive failures per endpoint. After threshold failures, circuit opens and stops routing for reset duration. Single probe tests recovery. |
| Retries with Backoff | `MaxRetries`, `RetryDelayMilliseconds` | `MeshInvocationClient.InvokeAsync` wraps HTTP calls in retry loop. Exponential backoff on transient codes (408, 429, 500, 502, 503, 504). Never retries 4xx client errors. |
| Active Health Checking | `HealthCheckEnabled`, `HealthCheckIntervalSeconds`, `HealthCheckTimeoutSeconds` | `MeshHealthCheckService` (BackgroundService) periodically probes registered endpoints. Unhealthy endpoints excluded from routing. |
| Load-Based Routing | `DefaultLoadBalancer`, `LoadThresholdPercent`, `DegradationThresholdSeconds` | `GetRouteAsync` filters degraded endpoints (stale `LastSeen`) and overloaded endpoints (above `LoadThresholdPercent`). Falls back to full list if all filtered. Supports configurable algorithm via `DefaultLoadBalancer`. |

### lib-contract: Validation and Safety Bounds (ALL COMPLETE)

| Feature | Properties | Implementation |
|---------|-----------|----------------|
| Validation Limits | `maxPartiesPerContract`, `maxMilestonesPerTemplate`, `maxPreboundApisPerMilestone`, `maxActiveContractsPerEntity` | Validation checks at start of `CreateTemplateAsync` and `CreateInstanceAsync`. Returns 400 with clear error messages when limits exceeded. |
| Consent Timeout | `defaultConsentTimeoutDays` | Proposals set `ExpiresAt = CreatedAt + defaultConsentTimeoutDays`. Lazy expiration checked on access. |
| Enforcement Mode | `defaultEnforcementMode` | Was already wired in `ReportBreachAsync` (line 186-188). Modes: advisory, event_only, consequence_based, community. |
| Prebound API Execution | `preboundApiBatchSize`, `preboundApiTimeoutMs` | Milestone completion chunks prebound APIs into batches, executes with `Task.WhenAll`, applies `CancellationTokenSource` with configured timeout. |

### lib-documentation: Content Management and Search (MOSTLY COMPLETE)

| Feature | Properties | Implementation |
|---------|-----------|----------------|
| Content Size Protection | `MaxContentSizeBytes` | Checks `Encoding.UTF8.GetByteCount(body.Content)` at start of `CreateDocumentAsync` and `UpdateDocumentAsync`. Returns 400 if exceeded. |
| Search Quality | `SearchCacheTtlSeconds`, `MinRelevanceScore`, `MaxSearchResults` | Results filtered by `MinRelevanceScore`, capped by `MaxSearchResults`, cached with `SearchCacheTtlSeconds` TTL keyed by query hash. |
| Voice Summary Length | `VoiceSummaryMaxLength` | Was already wired in voice summary generation (line 1773-1776). Truncates at word boundaries. |
| Search Index Rebuild | `SearchIndexRebuildOnStartup` | `SearchIndexRebuildService` (BackgroundService) discovers namespaces from `ALL_NAMESPACES_KEY` registry + `BINDINGS_REGISTRY_KEY`, calls `RebuildIndexAsync` per namespace on startup. |
| Git Operation Safety | `GitCloneTimeoutSeconds`, `GitStorageCleanupHours` | `GitSyncService.SyncRepositoryAsync` uses linked `CancellationTokenSource` with configured timeout. `RepositorySyncSchedulerService` runs cleanup loop deleting orphaned repo directories older than threshold. |
| Sync Operation Bounds | `MaxDocumentsPerSync`, `BulkOperationBatchSize` | Sync truncates file list with `.Take()` and skips orphan deletion when truncated. Bulk ops yield every `BulkOperationBatchSize` items via `Task.Yield()`. |
| Session Tracking TTL | `SessionTtlSeconds` | **REMOVED** - Property deleted from schema. Session tracking is not a planned feature. |

### lib-currency: Background Autogain (COMPLETE)

| Feature | Properties | Implementation |
|---------|-----------|----------------|
| Autogain Task Mode | `AutogainProcessingMode`, `AutogainTaskIntervalMs`, `AutogainBatchSize` | `CurrencyAutogainTaskService` (BackgroundService) runs when mode is "task". Iterates autogain-enabled currencies, processes wallets in configured batch sizes, applies gains proactively with configured interval between cycles. |

### lib-music: Cache Configuration (COMPLETE)

| Feature | Properties | Implementation |
|---------|-----------|----------------|
| Composition Cache TTL | `CompositionCacheTtlSeconds` | Schema created (`schemas/music-configuration.yaml`). Config class generated. `MusicService.cs` uses `_configuration.CompositionCacheTtlSeconds` instead of hardcoded `86400`. |

---

## Remaining Features

Three features remain unimplemented. All are low-to-medium priority and require external dependencies or significant new infrastructure.

### lib-documentation: AI-Powered Semantic Search

**Priority**: Medium
**Properties**: `AiEnhancementsEnabled`, `AiEmbeddingsModel`

**What it does**: When enabled, `/documentation/query` uses vector embeddings (generated by the configured model) to compute semantic similarity rather than keyword matching. "How do I deploy?" matches "Deployment Guide" even without shared keywords.

**Current state**: The architecture is already designed for this. `QueryAsync` in `SearchIndexService.cs:130` explicitly has `// Future: Add semantic/AI-based query processing when enabled` and falls back to keyword logic. The API already exposes separate endpoints:
- `/documentation/search` - keyword matching (stays as-is)
- `/documentation/query` - natural language (should use embeddings when enabled)

**Why it matters**: The documentation service is designed for AI agents (SWAIG, function calling, Claude tool use). Keyword search requires agents to guess exact terminology. Semantic search lets agents query naturally.

**Implementation scope**:
- When `AiEnhancementsEnabled` is true, generate embeddings for documents on index (using configured `AiEmbeddingsModel`)
- Store embedding vectors alongside documents in state
- In `QueryAsync`, embed the query and compute cosine similarity against stored vectors
- Return results ranked by semantic similarity instead of keyword TF-IDF
- **Blocker**: Requires choosing an embedding provider and adding the HTTP client dependency. No external AI service integration exists in Bannou yet.

---

### lib-achievement: Xbox Live Achievement Sync

**Priority**: Low
**Properties**: `XboxClientId`, `XboxClientSecret`

**What it does**: When configured, pushes unlocked achievements to Xbox Live using the Xbox Services API. Maps Bannou achievement definitions to Xbox achievement IDs and reports unlock events.

**Why it matters**: Players on Xbox expect achievements in their Xbox profile/gamerscore. Without this, achievements exist only within Bannou.

**Implementation scope**: Implement Xbox Live REST API client. On achievement unlock (when `AutoSyncOnUnlock` is true), map to Xbox equivalent and report. Use existing retry configuration (`SyncRetryAttempts`, `SyncRetryDelaySeconds`).

**Blocker**: Requires Xbox developer credentials and partner program enrollment. Cannot be tested without Xbox developer account access.

---

### lib-achievement: PlayStation Network Trophy Sync

**Priority**: Low
**Properties**: `PlayStationClientId`, `PlayStationClientSecret`

**What it does**: When configured, pushes unlocked achievements to PlayStation Network as trophy unlocks. Maps Bannou achievement definitions to PSN trophy IDs.

**Why it matters**: Platform parity with Xbox. PlayStation players expect trophies in their PSN profile.

**Implementation scope**: Same pattern as Xbox: on unlock, map to PSN trophy, report via API with retry logic.

**Blocker**: Requires PlayStation Partners program enrollment and NDA-protected SDK access. PSN trophy API is not publicly documented.

---

## Removed Properties

The following properties were removed from their configuration schemas as they provide no useful functionality:

| Service | Property | Reason Removed |
|---------|----------|---------------|
| Mesh | `EnableDetailedLogging` | Structured logging via ILogger already provides this; boolean toggle adds nothing |
| Mesh | `MetricsEnabled` | No metrics infrastructure (Prometheus/StatsD) exists in Bannou |
| Leaderboard | `RankCacheTtlSeconds` | Redis sorted sets are already sub-millisecond; application cache adds staleness without meaningful benefit |
| Documentation | `SessionTtlSeconds` | No session tracking feature exists or is planned |

*Note: StoreName properties (Achievement, Analytics, Leaderboard) were already removed in a prior cleanup.*

---

*Last verified: 2026-01-24. Build passes with 0 warnings, 0 errors. All completed features are wired and functional.*
