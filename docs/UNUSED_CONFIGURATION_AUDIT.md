# Unused Configuration Properties Audit

**Generated**: $(date -u +"%Y-%m-%d %H:%M:%S UTC")
**Purpose**: Track all configuration properties defined in schemas but not wired up in service code

## Status Legend
- ⏳ **PENDING** - Not yet reviewed
- ✅ **WIRED** - Successfully wired to service code
- ❌ **REMOVED** - Removed from schema (dead code)
- 🔄 **INFRASTRUCTURE** - Used by infrastructure, not service code
- ⚠️ **PLACEHOLDER** - Intentional placeholder for future feature

---

## lib-achievement (4 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ProgressCacheTtlSeconds | ⏳ | |
| RareThresholdPercent | ⏳ | |
| RarityCalculationIntervalMinutes | ⏳ | |
| RarityThresholdEarnedCount | ⏳ | |

## lib-actor (18 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ActorOperationTimeoutSeconds | ⏳ | |
| DefaultMemoryExpirationMinutes | ⏳ | |
| EncounterCacheTtlMinutes | ⏳ | |
| GoapMaxPlanDepth | ⏳ | |
| GoapPlanTimeoutMs | ⏳ | |
| GoapReplanThreshold | ⏳ | |
| MaxEncounterResultsPerQuery | ⏳ | |
| MaxPoolNodes | ⏳ | |
| MemoryStoreMaxRetries | ⏳ | |
| MessageQueueSize | ⏳ | |
| MinPoolNodes | ⏳ | |
| PersonalityCacheTtlMinutes | ⏳ | |
| PoolHealthCheckIntervalSeconds | ⏳ | |
| ScheduledEventCheckIntervalMilliseconds | ⏳ | |
| ControlPlaneAppId | ⏳ | |
| InstanceStatestoreName | ⏳ | |
| StateUpdateTransport | ⏳ | |
| PoolNodeImage | ⏳ | |

## lib-asset (13 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultBundleCacheTtlHours | ⏳ | |
| MetabundleJobTtlSeconds | ⏳ | |
| ProcessingBatchIntervalSeconds | ⏳ | |
| ProcessingJobMaxWaitSeconds | ⏳ | |
| ProcessingJobPollIntervalSeconds | ⏳ | |
| ProcessingMaxRetries | ⏳ | |
| ProcessingQueueCheckIntervalSeconds | ⏳ | |
| ProcessingRetryDelaySeconds | ⏳ | |
| ProcessorAvailabilityMaxWaitSeconds | ⏳ | |
| ProcessorAvailabilityPollIntervalSeconds | ⏳ | |
| ShutdownDrainIntervalSeconds | ⏳ | |
| ShutdownDrainTimeoutMinutes | ⏳ | |
| ZipCacheTtlHours | ⏳ | |

## lib-behavior (2 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| CompilerMaxConstants | ⏳ | |
| CompilerMaxStrings | ⏳ | |

## lib-character (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| CharacterListUpdateMaxRetries | ⏳ | |

## lib-character-encounter (2 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultPageSize | ⏳ | |
| MemoryRefreshBoost | ⏳ | |

## lib-connect (11 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| BufferSize | ⏳ | |
| ConnectionShutdownTimeoutSeconds | ⏳ | |
| ConnectionTimeoutSeconds | ⏳ | |
| HeartbeatIntervalSeconds | ⏳ | |
| HttpClientTimeoutSeconds | ⏳ | |
| MaxConcurrentConnections | ⏳ | |
| MaxMessagesPerMinute | ⏳ | |
| MessageQueueSize | ⏳ | |
| RateLimitWindowMinutes | ⏳ | |
| ReconnectionWindowExtensionMinutes | ⏳ | |
| WebSocketKeepAliveIntervalSeconds | ⏳ | |

## lib-contract (7 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultConsentTimeoutDays | ⏳ | |
| MaxActiveContractsPerEntity | ⏳ | |
| MaxMilestonesPerTemplate | ⏳ | |
| MaxPartiesPerContract | ⏳ | |
| MaxPreboundApisPerMilestone | ⏳ | |
| PreboundApiBatchSize | ⏳ | |
| PreboundApiTimeoutMs | ⏳ | |

## lib-currency (4 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| AutogainBatchSize | ⏳ | |
| AutogainTaskIntervalMs | ⏳ | |
| HoldMaxDurationDays | ⏳ | |
| IdempotencyTtlSeconds | ⏳ | |

## lib-documentation (8 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| BulkOperationBatchSize | ⏳ | |
| GitCloneTimeoutSeconds | ⏳ | |
| MaxContentSizeBytes | ⏳ | |
| MaxDocumentsPerSync | ⏳ | |
| MaxSearchResults | ⏳ | |
| SearchCacheTtlSeconds | ⏳ | |
| SessionTtlSeconds | ⏳ | |
| VoiceSummaryMaxLength | ⏳ | |

## lib-game-session (2 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultSessionTimeoutSeconds | ⏳ | |
| MaxPlayersPerSession | ⏳ | |

## lib-leaderboard (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| RankCacheTtlSeconds | ⏳ | |

## lib-mapping (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| AuthorityHeartbeatIntervalSeconds | ⏳ | |

## lib-mesh (12 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| CircuitBreakerThreshold | ⏳ | |
| ConnectTimeoutSeconds | ⏳ | |
| DegradationThresholdSeconds | ⏳ | |
| EndpointCacheTtlSeconds | ⏳ | |
| HealthCheckIntervalSeconds | ⏳ | |
| HealthCheckTimeoutSeconds | ⏳ | |
| HeartbeatIntervalSeconds | ⏳ | |
| LoadThresholdPercent | ⏳ | |
| MaxRetries | ⏳ | |
| MaxServiceMappingsDisplayed | ⏳ | |
| PooledConnectionLifetimeMinutes | ⏳ | |
| RetryDelayMilliseconds | ⏳ | |

## lib-messaging (7 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ConnectionRetryCount | ⏳ | |
| ConnectionRetryDelayMs | ⏳ | |
| ConnectionTimeoutSeconds | ⏳ | |
| RabbitMQNetworkRecoveryIntervalSeconds | ⏳ | |
| RequestTimeoutSeconds | ⏳ | |
| RetryDelayMs | ⏳ | |
| RetryMaxAttempts | ⏳ | |

## lib-save-load (10 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ConflictDetectionWindowMinutes | ⏳ | |
| MaxConcurrentUploads | ⏳ | |
| MaxSaveSizeBytes | ⏳ | |
| MaxSavesPerMinute | ⏳ | |
| MaxSlotsPerOwner | ⏳ | |
| MaxTotalSizeBytesPerOwner | ⏳ | |
| SessionCleanupGracePeriodMinutes | ⏳ | |
| ThumbnailMaxSizeBytes | ⏳ | |
| ThumbnailUrlTtlMinutes | ⏳ | |
| UploadRetryDelayMs | ⏳ | |

## lib-scene (7 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| CheckoutExpirationCheckIntervalSeconds | ⏳ | |
| CheckoutHeartbeatIntervalSeconds | ⏳ | |
| DefaultMaxReferenceDepth | ⏳ | |
| DefaultVersionRetentionCount | ⏳ | |
| MaxSceneSizeBytes | ⏳ | |
| MaxTagsPerNode | ⏳ | |
| MaxTagsPerScene | ⏳ | |

## lib-state (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| ConnectRetryCount | ⏳ | |

## lib-voice (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| KamailioRequestTimeoutSeconds | ⏳ | |

---

## Summary

| Category | Count |
|----------|-------|
| Total Unused Tunables | 107 |
| Wired | 0 |
| Removed | 0 |
| Infrastructure | 0 |
| Placeholder | 0 |
| Pending | 107 |

---

*This file tracks T21 compliance work. Update status as each property is addressed.*
