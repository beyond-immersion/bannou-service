# Unused Configuration Properties Audit

**Updated**: 2026-01-23
**Purpose**: Track all configuration properties defined in schemas but not wired up in service code

## Status Legend
- ⏳ **PENDING** - Not yet reviewed
- ✅ **WIRED** - Successfully wired to service code
- ❌ **REMOVED** - Removed from schema (dead code)
- 🔄 **INFRASTRUCTURE** - Used by infrastructure, not service code
- ⚠️ **PLACEHOLDER** - Intentional placeholder for future feature
- 🔧 **STRUCTURAL** - Requires structural changes to wire

---

## lib-achievement (4 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ProgressTtlSeconds | ✅ | Wired to SaveAsync TTL (0 = no expiry, permanent storage) |
| RarityThresholdEarnedCount | ✅ | Wired to IsRare calculation |
| RareThresholdPercent | ⚠️ | Placeholder for unimplemented rarity percentage system |
| RarityCalculationIntervalMinutes | ⚠️ | Placeholder for unimplemented rarity calculation background task |

## lib-actor (18 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| PersonalityCacheTtlMinutes | ✅ | Wired to PersonalityCache |
| EncounterCacheTtlMinutes | ✅ | Wired to EncounterCache |
| MaxEncounterResultsPerQuery | ✅ | Wired to EncounterCache (schema default updated to 50) |
| PoolHealthCheckIntervalSeconds | ✅ | Wired to PoolHealthMonitor (schema default updated to 15) |
| ScheduledEventCheckIntervalMilliseconds | ✅ | Wired to ScheduledEventManager |
| ActorOperationTimeoutSeconds | ⏳ | |
| DefaultMemoryExpirationMinutes | ⏳ | |
| GoapMaxPlanDepth | ⏳ | |
| GoapPlanTimeoutMs | ⏳ | |
| GoapReplanThreshold | ⏳ | |
| MaxPoolNodes | ⏳ | |
| MemoryStoreMaxRetries | ⏳ | |
| MessageQueueSize | ⏳ | |
| MinPoolNodes | ⏳ | |
| ControlPlaneAppId | ⏳ | |
| InstanceStatestoreName | ⏳ | |
| StateUpdateTransport | ⏳ | |
| PoolNodeImage | ⏳ | |

## lib-asset (13 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| ProcessorAvailabilityMaxWaitSeconds | ✅ | Wired to EnsureProcessorAvailable |
| ProcessorAvailabilityPollIntervalSeconds | ✅ | Wired to EnsureProcessorAvailable |
| ProcessingMaxRetries | ✅ | Wired to DelegateToProcessingPool and UpdatePoolIndexAsync |
| ProcessingRetryDelaySeconds | ✅ | Wired to DelegateToProcessingPool |
| ShutdownDrainTimeoutMinutes | ✅ | Wired to AssetProcessingWorker.ShutdownAsync |
| ShutdownDrainIntervalSeconds | ✅ | Wired to AssetProcessingWorker.ShutdownAsync |
| ProcessingJobMaxWaitSeconds | ⚠️ | Placeholder for unimplemented sync processing wait |
| ProcessingQueueCheckIntervalSeconds | ⚠️ | Placeholder for unimplemented queue polling |
| DefaultBundleCacheTtlHours | ⏳ | |
| MetabundleJobTtlSeconds | ⏳ | |
| ProcessingBatchIntervalSeconds | ⏳ | |
| ProcessingJobPollIntervalSeconds | ⏳ | |
| ZipCacheTtlHours | ⏳ | |

## lib-behavior (2 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| CompilerMaxConstants | 🔄 | VM architecture constant (byte index = max 256), defined in VmConfig |
| CompilerMaxStrings | 🔄 | VM architecture constant (ushort = max 65536), defined in VmConfig |

## lib-character (1 tunable)

| Property | Status | Notes |
|----------|--------|-------|
| CharacterListUpdateMaxRetries | ⚠️ | Placeholder - no character list operations implemented |

## lib-character-encounter (2 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultPageSize | ⚠️ | Schema has API default, config version redundant |
| MemoryRefreshBoost | ⚠️ | Schema has API default, config version redundant |

## lib-connect (11 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| BufferSize | ✅ | Wired to WebSocket buffer allocation (2 places) |
| HeartbeatIntervalSeconds | ✅ | Wired to periodic heartbeat update |
| MaxMessagesPerMinute | ✅ | Wired to MessageRouter.CheckRateLimit |
| HttpClientTimeoutSeconds | 🔧 | Set at plugin ConfigureServices time, before config resolved |
| ConnectionShutdownTimeoutSeconds | 🔧 | Requires structural changes |
| ConnectionTimeoutSeconds | 🔧 | Requires structural changes |
| MaxConcurrentConnections | 🔧 | Requires enforcement logic |
| MessageQueueSize | 🔧 | Requires structural changes |
| RateLimitWindowMinutes | 🔧 | Requires structural changes to rate limiter |
| ReconnectionWindowExtensionMinutes | ⏳ | |
| WebSocketKeepAliveIntervalSeconds | 🔧 | Requires WebSocket options configuration |

## lib-contract (7 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| DefaultConsentTimeoutDays | ⚠️ | Placeholder - consent timeout not enforced |
| MaxActiveContractsPerEntity | ⚠️ | Placeholder - limit not enforced |
| MaxMilestonesPerTemplate | ⚠️ | Placeholder - limit not enforced |
| MaxPartiesPerContract | ⚠️ | Placeholder - limit not enforced |
| MaxPreboundApisPerMilestone | ⚠️ | Placeholder - limit not enforced |
| PreboundApiBatchSize | ⚠️ | Placeholder - prebound API batching not implemented |
| PreboundApiTimeoutMs | ⚠️ | Placeholder - prebound API calling not implemented |

## lib-currency (4 tunables)

| Property | Status | Notes |
|----------|--------|-------|
| AutogainBatchSize | ⚠️ | Placeholder - autogain background task not implemented |
| AutogainTaskIntervalMs | ⚠️ | Placeholder - autogain background task not implemented |
| HoldMaxDurationDays | ⚠️ | Placeholder - hold duration limit not enforced |
| IdempotencyTtlSeconds | ⚠️ | Placeholder - idempotency checking not implemented |

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
| PooledConnectionLifetimeMinutes | ✅ | Wired to MeshInvocationClient SocketsHttpHandler |
| ConnectTimeoutSeconds | ✅ | Wired to MeshInvocationClient SocketsHttpHandler |
| EndpointCacheTtlSeconds | ✅ | Wired to MeshInvocationClient EndpointCache |
| CircuitBreakerThreshold | ⏳ | |
| DegradationThresholdSeconds | ⏳ | |
| HealthCheckIntervalSeconds | ⏳ | |
| HealthCheckTimeoutSeconds | ⏳ | |
| HeartbeatIntervalSeconds | ⏳ | |
| LoadThresholdPercent | ⏳ | |
| MaxRetries | ⏳ | |
| MaxServiceMappingsDisplayed | ⏳ | |
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
| Wired | 18 |
| Infrastructure | 2 |
| Placeholder | 22 |
| Structural | 7 |
| Pending | 58 |

---

*This file tracks T21 compliance work. Update status as each property is addressed.*
