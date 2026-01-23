# Missing Implementation Features

> **Purpose**: Configuration properties that represent genuinely useful features we intended to have but never implemented.
> **Source**: Analysis of unwired configuration properties across all services.
> **Action**: Each feature below should be implemented when its service is next being worked on.

---

## lib-mesh: Production Resilience Features

The mesh service has solid basic routing but lacks production-grade resilience patterns. These 11 unwired properties represent three cohesive feature groups that prevent cascading failures and improve reliability.

### Circuit Breaker Pattern

**Properties**: `CircuitBreakerEnabled`, `CircuitBreakerThreshold`, `CircuitBreakerResetSeconds`

**What it does**: Tracks consecutive failures per endpoint. After `CircuitBreakerThreshold` failures (default 5), the circuit "opens" and stops routing to that endpoint for `CircuitBreakerResetSeconds` (default 30). After the reset period, a single probe request tests recovery.

**Why it matters**: Without circuit breaking, a failing endpoint continues receiving traffic, causing latency spikes and resource exhaustion across the mesh. This is the single most important production resilience feature missing from lib-mesh.

**Implementation scope**: Track failure counts per endpoint in the `MeshStateManager`. Before routing, check circuit state. On failure response from `MeshInvocationClient`, increment counter. Use `IStateStore` with TTL for automatic circuit reset.

### Automatic Retries with Backoff

**Properties**: `MaxRetries`, `RetryDelayMilliseconds`

**What it does**: On transient failures (timeouts, 503s), automatically retries the request up to `MaxRetries` times (default 3) with exponential backoff starting at `RetryDelayMilliseconds` (default 100ms, doubling each retry).

**Why it matters**: Transient failures from pod restarts, network blips, and GC pauses are common in distributed systems. Without retries, every transient failure surfaces to the caller as a hard error.

**Implementation scope**: Wrap the HTTP call in `MeshInvocationClient.InvokeAsync` with a retry loop. Only retry on transient status codes (408, 429, 500, 502, 503, 504) and `HttpRequestException`. Never retry on 4xx client errors.

### Active Health Checking

**Properties**: `HealthCheckEnabled`, `HealthCheckIntervalSeconds`, `HealthCheckTimeoutSeconds`

**What it does**: Periodically probes registered endpoints with lightweight health requests (every `HealthCheckIntervalSeconds`, default 60s). Endpoints that fail to respond within `HealthCheckTimeoutSeconds` (default 5s) are marked unhealthy and excluded from routing.

**Why it matters**: Currently, mesh only knows an endpoint is unhealthy when a real request fails. Active health checking detects failures proactively, so the first real request after a failure doesn't have to eat the latency penalty.

**Implementation scope**: Background `IHostedService` that iterates registered endpoints and issues GET requests to a health path. Update endpoint health status in state store. Routing logic filters out unhealthy endpoints.

### Load-Based Routing

**Properties**: `DefaultLoadBalancer`, `LoadThresholdPercent`, `DegradationThresholdSeconds`

**What it does**: Routes requests based on reported load rather than pure round-robin. `DefaultLoadBalancer` selects the algorithm (RoundRobin, LeastConnections, Weighted, Random). `LoadThresholdPercent` (default 80%) filters out overloaded endpoints. `DegradationThresholdSeconds` (default 60s) marks endpoints as degraded when heartbeats are late.

**Why it matters**: Round-robin distributes traffic evenly but ignores capacity differences. A slow endpoint gets the same traffic as a fast one, causing uneven latency. Load-aware routing sends traffic where it can be served fastest.

**Implementation scope**: The heartbeat endpoint already accepts load metrics. The routing logic in `GetOptimalEndpointAsync` needs to filter by `LoadThresholdPercent` and select based on the configured algorithm.

---

## lib-contract: Validation and Safety Bounds

The contract service has zero references to its configuration class. All 8 properties represent safety bounds and behavioral configuration that should constrain the service. Without them, contracts have no limits on size, no timeouts on consent, and no configurable enforcement.

### Validation Limits

**Properties**: `maxPartiesPerContract`, `maxMilestonesPerTemplate`, `maxPreboundApisPerMilestone`, `maxActiveContractsPerEntity`

**What it does**: Prevents unbounded resource consumption by limiting contract complexity:
- Max 20 parties per contract (prevents fan-out explosions in consent flow)
- Max 50 milestones per template (prevents unbounded state tracking)
- Max 10 prebound APIs per milestone (prevents API spam on milestone completion)
- Max 100 active contracts per entity (prevents entity from being locked by infinite obligations)

**Why it matters**: Without these limits, a malicious or buggy client can create contracts with thousands of parties or milestones, causing memory and processing issues. These are standard API safety guards.

**Implementation scope**: Add validation checks at the start of `CreateTemplateAsync` and `CreateInstanceAsync`. Return 400 with clear error messages when limits are exceeded.

### Consent Timeout

**Properties**: `defaultConsentTimeoutDays`

**What it does**: Proposals that haven't received all party consent within `defaultConsentTimeoutDays` (default 7) are automatically expired.

**Why it matters**: Without a timeout, proposed contracts that never get fully consented remain in "pending_consent" state forever, consuming storage and cluttering queries. A 7-day default gives parties reasonable time while preventing indefinite accumulation.

**Implementation scope**: When creating an instance, set `ExpiresAt = CreatedAt + defaultConsentTimeoutDays`. Either use a background cleanup task or check expiry on access (lazy expiration pattern matching existing TTL patterns).

### Enforcement Mode

**Properties**: `defaultEnforcementMode`

**What it does**: Configures how contract breaches are handled system-wide:
- `advisory` - Breaches are recorded but have no mechanical effect
- `event_only` - Breaches publish events that other services can react to
- `consequence_based` - Breaches trigger prebound consequence APIs
- `community` - Breaches affect reputation/standing systems

**Why it matters**: Different game contexts need different enforcement strengths. During development, advisory mode lets designers test contracts without consequences. In production, consequence-based mode gives contracts real teeth.

**Implementation scope**: Read mode in `ReportBreachAsync`. Based on mode, either just record the breach, publish the breach event, or also execute consequence APIs defined in the template.

### Prebound API Execution Control

**Properties**: `preboundApiBatchSize`, `preboundApiTimeoutMs`

**What it does**: When milestones complete and trigger prebound API calls, execute them in batches of `preboundApiBatchSize` (default 10) with individual timeout of `preboundApiTimeoutMs` (default 30000ms).

**Why it matters**: A milestone with many prebound APIs could overwhelm downstream services if all fired simultaneously. Batching provides backpressure. The timeout prevents a hung downstream service from blocking milestone completion indefinitely.

**Implementation scope**: In milestone completion logic, chunk the prebound APIs into batches, execute each batch in parallel with `Task.WhenAll`, and apply `CancellationTokenSource` with the configured timeout.

---

## lib-documentation: Content Management and Search

The documentation service has 7 wired properties (git sync, trashcan TTL, import limits, git path) but 14 unwired properties that represent search quality, content safety, and operational features.

### Content Size Protection

**Properties**: `MaxContentSizeBytes`

**What it does**: Rejects document creation/update requests where content exceeds 500KB (default). Returns 400 with size information.

**Why it matters**: Without size limits, a single bulk import or API call can store arbitrarily large documents, exhausting state store capacity and causing slow queries. This is standard API input validation.

**Implementation scope**: Check `body.Content.Length` (as UTF-8 bytes) at the start of `CreateDocumentAsync` and `UpdateDocumentAsync`. Return 400 if exceeded.

### Search Quality Configuration

**Properties**: `SearchCacheTtlSeconds`, `MinRelevanceScore`, `MaxSearchResults`

**What it does**:
- Cache search results for 300 seconds (default) to reduce computation on repeated queries
- Filter out results below 0.3 relevance score (default) to avoid noise
- Cap results at 20 (default) to keep responses bounded

**Why it matters**: The search implementation currently returns unbounded results with no caching. Repeated identical queries recompute from scratch. Low-relevance results dilute useful content.

**Implementation scope**:
- `MaxSearchResults`: Add `.Take(_configuration.MaxSearchResults)` to search result lists
- `MinRelevanceScore`: Filter results where `score < _configuration.MinRelevanceScore`
- `SearchCacheTtlSeconds`: Cache search results keyed by query hash with TTL

### Voice Summary Length

**Properties**: `VoiceSummaryMaxLength`

**What it does**: Truncates voice-friendly summaries to 200 characters (default). The documentation service is designed for voice agents (SWAIG/function calling) - summaries need to be speakable.

**Why it matters**: Voice interfaces have practical limits on response length. A 2000-character summary is useless for a voice agent. This ensures summaries are concise enough to speak aloud.

**Implementation scope**: When generating `voice_summary` in query/search responses, truncate to `_configuration.VoiceSummaryMaxLength` at word boundaries.

### Search Index Rebuild

**Properties**: `SearchIndexRebuildOnStartup`

**What it does**: When true (default), rebuilds the in-memory search index from all stored documents on service startup.

**Why it matters**: After a restart, the search index is empty. Without rebuild, search returns no results until documents are individually accessed or new ones are created.

**Implementation scope**: In service initialization or a startup `IHostedService`, load all documents from state and populate the search index. Consider doing this lazily or in a background task to avoid blocking startup.

### Session Tracking TTL

**Properties**: `SessionTtlSeconds`

**What it does**: Sets TTL for informal session tracking entries (default 24 hours). Sessions track which documents a user has viewed for suggestion relevance.

**Why it matters**: Without TTL, session data accumulates indefinitely. The 24-hour default means suggestions are based on recent browsing context, not stale history from weeks ago.

**Implementation scope**: When storing session access records, use `StateOptions { Ttl = _configuration.SessionTtlSeconds }`.

### Git Operation Safety

**Properties**: `GitCloneTimeoutSeconds`, `GitStorageCleanupHours`

**What it does**:
- Clone/pull operations timeout after 300 seconds (default) to prevent hangs on unreachable repos
- Inactive cloned repos are cleaned up after 24 hours (default) to prevent disk exhaustion

**Why it matters**: A git clone to an unresponsive server blocks the sync thread indefinitely without a timeout. Cloned repos that are no longer bound accumulate on disk without cleanup.

**Implementation scope**:
- Wrap git process execution with `CancellationTokenSource` using configured timeout
- Background cleanup task that removes repo directories older than `GitStorageCleanupHours`

### Sync Operation Bounds

**Properties**: `MaxDocumentsPerSync`, `BulkOperationBatchSize`

**What it does**:
- Limits documents processed per sync to 1000 (default) to prevent memory spikes on large repos
- Bulk operations (delete, update) process in batches of 10 (default) to avoid transaction timeouts

**Why it matters**: A repository with 50,000 markdown files would exhaust memory if synced in one pass. Bulk deletes of thousands of documents could timeout the state store connection.

**Implementation scope**:
- In sync logic, paginate document processing with `Take(_configuration.MaxDocumentsPerSync)`
- In bulk operations, chunk items and process sequentially with configured batch size

### AI-Powered Semantic Search

**Properties**: `AiEnhancementsEnabled`, `AiEmbeddingsModel`

**What it does**: When enabled, the `/documentation/query` endpoint uses vector embeddings (generated by the configured model) to compute semantic similarity rather than keyword matching. This makes "how do I deploy?" match documents titled "Deployment Guide" even without shared keywords.

**Current state**: The architecture is already designed for this. `QueryAsync` in `SearchIndexService.cs:130` explicitly has `// Future: Add semantic/AI-based query processing when enabled` and currently falls back to the same keyword logic as `SearchAsync`. The API already exposes separate endpoints:
- `/documentation/search` - keyword matching (stays as-is)
- `/documentation/query` - natural language (should use embeddings when enabled)

**Why it matters**: The documentation service is designed for AI agents (SWAIG, function calling, Claude tool use). Keyword search requires agents to guess exact terminology. Semantic search lets agents query naturally and find relevant documentation regardless of phrasing.

**Implementation scope**:
- When `AiEnhancementsEnabled` is true, generate embeddings for documents on index (using configured `AiEmbeddingsModel`)
- Store embedding vectors alongside documents in state
- In `QueryAsync`, embed the query and compute cosine similarity against stored vectors
- Return results ranked by semantic similarity instead of keyword TF-IDF

---

## lib-currency: Background Autogain Processing

The currency service has a fully-implemented lazy autogain mode but the planned background task mode is unwired.

### Autogain Task Mode

**Properties**: `AutogainProcessingMode`, `AutogainTaskIntervalMs`, `AutogainBatchSize`

**What it does**: Two modes for calculating autogain (passive income on currency balances):
- `lazy` (implemented): Gain is calculated on-demand when a balance is queried via `ApplyAutogainIfNeededAsync`
- `task` (not implemented): A background `IHostedService` periodically iterates all wallets with autogain-enabled currencies and applies gains proactively

The task mode would process wallets in batches of `AutogainBatchSize` (default 1000) every `AutogainTaskIntervalMs` (default 60000ms).

**Why it matters**: With lazy-only mode:
- Global supply statistics (`/currency/stats/global-supply`) are stale for unqueried wallets
- Autogain cap events can't fire until someone happens to query the wallet
- Leaderboards based on currency balances are inaccurate for dormant players
- Any system that needs accurate balances across all wallets (analytics, taxation, inflation tracking) gets incorrect data

**Implementation scope**: Create a background `IHostedService` (matching the pattern in `DocumentationService`'s `RepositorySyncSchedulerService`). When `AutogainProcessingMode == "task"`:
- Query all currencies with `AutogainEnabled == true`
- For each, iterate wallets in batches of `AutogainBatchSize`
- Call `ApplyAutogainIfNeededAsync` for each balance
- Sleep `AutogainTaskIntervalMs` between cycles

---

## lib-achievement: Platform Achievement Sync

The achievement service has Steam sync stubs and configuration, but the Xbox Live and PlayStation Network integrations need implementation to match.

### Xbox Live Achievement Sync

**Properties**: `XboxClientId`, `XboxClientSecret`

**What it does**: When configured, pushes unlocked achievements to Xbox Live using the Xbox Services API. Maps Bannou achievement definitions to Xbox achievement IDs and reports unlock events.

**Why it matters**: Players on Xbox expect their achievements to appear in their Xbox profile. Without this, achievements exist only within Bannou and don't sync to the platform gamerscore system.

**Implementation scope**: Implement Xbox Live REST API client (or use official Xbox Services SDK). On achievement unlock (when `AutoSyncOnUnlock` is true), map the achievement to its Xbox equivalent and report the unlock. Use the existing retry configuration (`SyncRetryAttempts`, `SyncRetryDelaySeconds`).

### PlayStation Network Trophy Sync

**Properties**: `PlayStationClientId`, `PlayStationClientSecret`

**What it does**: When configured, pushes unlocked achievements to PlayStation Network as trophy unlocks. Maps Bannou achievement definitions to PSN trophy IDs.

**Why it matters**: Same as Xbox - PlayStation players expect trophies to appear in their PSN profile. Platform parity requires both integrations.

**Implementation scope**: Implement PSN trophy API client. Same pattern as Xbox: on unlock, map to PSN trophy, report via API with retry logic.

---

## lib-music: Cache Configuration

The music service has no configuration schema at all, yet contains a hardcoded cache TTL.

### Composition Cache TTL

**Current state**: Hardcoded `86400` (24 hours) in `MusicService.cs:1245`

**What it does**: Deterministic compositions (those with explicit seeds) produce identical output every time. Caching them avoids recomputation.

**Why it matters**: The 24-hour TTL is reasonable but not configurable. In development, a shorter TTL helps test generation changes. In production with heavy load, a longer TTL reduces computation.

**Implementation scope**:
1. Create `schemas/music-configuration.yaml` with `CompositionCacheTtlSeconds` (default 86400)
2. Run generation to create `MusicServiceConfiguration`
3. Replace hardcoded `86400` with `_configuration.CompositionCacheTtlSeconds`

---

## Properties to REMOVE (Not Useful Features)

The following properties were removed from their configuration schemas:

| Service | Property | Reason Removed |
|---------|----------|---------------|
| Mesh | `EnableDetailedLogging` | Structured logging via ILogger already provides this; boolean toggle adds nothing |
| Mesh | `MetricsEnabled` | No metrics infrastructure (Prometheus/StatsD) exists in Bannou |
| Leaderboard | `RankCacheTtlSeconds` | Redis sorted sets are already sub-millisecond; application cache adds staleness without meaningful benefit |

*Note: StoreName properties (Achievement, Analytics, Leaderboard) were already removed in a prior cleanup.*

---

## Implementation Priority

| Priority | Service | Feature Group | Rationale |
|----------|---------|---------------|-----------|
| **High** | lib-mesh | Circuit Breaker | Prevents cascading failures in production |
| **High** | lib-mesh | Retries with Backoff | Handles transient failures gracefully |
| **High** | lib-contract | Validation Limits | Safety bounds prevent resource exhaustion |
| **Medium** | lib-contract | Consent Timeout | Prevents infinite pending state accumulation |
| **Medium** | lib-documentation | Content Size Protection | DOS prevention at API boundary |
| **Medium** | lib-documentation | Search Quality | Bounded results and relevance filtering |
| **Medium** | lib-mesh | Active Health Checking | Proactive failure detection |
| **Medium** | lib-currency | Autogain Task Mode | Accurate global supply, leaderboards, cap events |
| **Medium** | lib-documentation | AI Semantic Search | Core differentiator for AI agent documentation queries |
| **Low** | lib-mesh | Load-Based Routing | Optimization; round-robin works adequately |
| **Low** | lib-contract | Enforcement Mode | Advisory mode is functional default |
| **Low** | lib-contract | Prebound API Execution | Batching is optimization, not correctness |
| **Low** | lib-documentation | Git Operation Safety | Timeouts and cleanup are operational concerns |
| **Low** | lib-documentation | Session/Index/Voice | Quality-of-life improvements |
| **Low** | lib-achievement | Xbox Live Sync | Platform parity for Xbox players |
| **Low** | lib-achievement | PlayStation Sync | Platform parity for PlayStation players |
| **Low** | lib-music | Cache TTL | Functional as hardcoded; configurability is nice-to-have |

---

*Generated from analysis of unwired configuration properties across 20+ services. Services not listed (matchmaking, game-session, analytics, leaderboard, world-data services) either have fully-wired configurations or empty configurations appropriate to their CRUD nature.*
