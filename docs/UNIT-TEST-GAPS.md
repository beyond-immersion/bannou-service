# Unit Test Coverage Gaps

> **Generated**: 2026-03-02
> **Purpose**: Authoritative gap list for unit test hardening work
> **Status**: Tasks #1 (mesh) and #2 (messaging) COMPLETED. All others pending.

---

## Task #1: L0 Mesh — 19 gaps [COMPLETED]

47 new tests added, all 143 tests passing.

1. **MeshInvocationClient.InvokeMethodAsync (3 overloads)** — Typed invoke overloads (lines 83-254) that deserialize responses, handle ApiException, and perform circuit breaker + retry logic have zero test coverage.
2. **MeshInvocationClient.InvokeRawAsync** — Raw invocation method (lines 256-336) bypassing circuit breaker is entirely untested.
3. **MeshInvocationClient.InvokeMethodWithResponseAsync retry/backoff logic** — Exponential backoff retry, circuit breaker state checking, and multi-endpoint fallback with endpointIndex rotation (lines 338-540) are untested.
4. **MeshInvocationClient.ResolveEndpointAsync** — Endpoint resolution with TTL-based EndpointCache (lines 542-636) is entirely untested.
5. **MeshInvocationClient.BuildTargetUri** — URI construction handling port 80/443 omission (lines 610-628) has no tests.
6. **MeshInvocationClient.EndpointCache** — Inner cache class with TTL expiration, max size enforcement, LRU-style eviction (lines 630-838) has no tests.
7. **MeshStateManager — all operational methods** — RegisterEndpointAsync, DeregisterEndpointAsync, UpdateHeartbeatAsync, GetEndpointsForAppIdAsync, GetAllEndpointsAsync, GetEndpointByInstanceIdAsync, CheckHealthAsync all have zero coverage.
8. **MeshStateManager.InitializeAsync success path** — Only failure path is tested.
9. **LocalMeshStateManager** — Entire class (136 lines) has zero test coverage.
10. **MeshService.GetRouteAsync load balancing algorithms** — 5 strategies (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random) lack dedicated edge-case tests.
11. **MeshService.FormatUptime** — Uptime formatting helper (lines 700-734) has no dedicated test.
12. **MeshServicePlugin.RegisterMeshEndpointAsync** — Self-registration method (lines 121-169) untested.
13. **MeshServicePlugin.ConfigureServices DI registration** — IMeshInstanceIdentifier factory, IMeshStateManager factory, MeshInvocationClient factory, MeshHealthCheckService registration untested.
14. **MeshHealthCheckService.ExecuteAsync full loop** — Main loop (lines 70-130) only tested in "disabled" case.
15. **MeshHealthCheckService.ProbeEndpointAsync healthy path** — Only "shutting down" skip path tested.
16. **MeshServiceEvents.TryPublishDegradationEventAsync deduplication** — Deduplication window (skipping duplicates within configured window) not explicitly tested.
17. **MeshLuaScripts** — Script loading/caching mechanism (80 lines) untested.
18. **DistributedCircuitBreaker.RecordFailureAsync local-only mode** — Fallback path when Redis unavailable (lines 180-220) has no dedicated test.
19. **DistributedCircuitBreaker auto-transition HalfOpen to Closed** — GetStateAsync auto-transition on timeout (lines 80-130) untested.

---

## Task #2: L0 Messaging — 24 gaps [COMPLETED]

14 new tests added, all 247 tests passing. Gaps #9-20 (RabbitMQ), #22 (retry buffer), #23 (plugin DI) are infrastructure-dependent and not unit-testable.

1. **MessagingService.RecoverExternalSubscriptionsAsync** — Full recovery flow (lines 280-380) iterating stored subscriptions and re-establishing them is untested.
2. **MessagingService.CreateCallbackHandler retry logic** — Callback handler factory (lines 382-460) with retry loop, exponential backoff, and failure handling is untested.
3. **MessagingService.RefreshSubscriptionTtlAsync** — TTL refresh method (lines 462-500) is untested.
4. **MessagingService.DisposeAsync** — Internal logic for unsubscribing all tracked subscriptions not thoroughly tested (partial failure, empty list).
5. **MessagingSubscriptionRecoveryService** — Entire background service (61 lines) has no dedicated test file.
6. **DeadLetterConsumerService.ProcessDeadLetterAsync** — Actual processing method (lines 176-277) extracting metadata, logging, publishing error events, acking/nacking is untested.
7. **DeadLetterConsumerService.ExecuteAsync full lifecycle** — Startup path with exchange declaration, queue binding, consumer registration is untested (only "disabled" case tested).
8. **DeadLetterConsumerService.StopAsync channel cleanup** — Channel close failure path is untested.
9. **RabbitMQMessageBus.TryPublishAsync** — Full publish flow (lines 123-380) including channel acquisition, serialization, exchange declaration, publish, channel return is entirely untested.
10. **RabbitMQMessageBus.TryPublishRawAsync** — Raw byte publish method (lines 381-486) untested.
11. **RabbitMQMessageBus.TryPublishErrorAsync** — Error event construction with deduplication and instance identity (lines 487-748) untested.
12. **RabbitMQMessageSubscriber.SubscribeAsync** — Full subscription flow (lines 74-206) with queue declaration, exchange binding, consumer setup untested.
13. **RabbitMQMessageSubscriber.SubscribeDynamicAsync** — Dynamic subscription method (lines 207-343) untested.
14. **RabbitMQMessageSubscriber.SubscribeDynamicRawAsync** — Raw dynamic subscription (lines 344-503) untested.
15. **RabbitMQMessageSubscriber.UnsubscribeAsync** — Unsubscribe flow (lines 504-586) untested.
16. **RabbitMQConnectionManager.GetChannelAsync** — Channel pool management (lines 167-301) with pool draining, creation, prefetch, max channel enforcement untested.
17. **RabbitMQConnectionManager.ReturnChannelAsync** — Channel return logic (lines 302-348) untested.
18. **RabbitMQConnectionManager.CreateConsumerChannelAsync** — Consumer channel creation (lines 349-394) untested.
19. **RabbitMQConnectionManager.InitializeAsync** — Connection initialization with retry (lines 95-166) only tested at interface level.
20. **RabbitMQMessageTap.CreateTapAsync** — Tap creation flow (lines 62-409) untested at RabbitMQ level.
21. **InMemoryMessageBus.TryPublishErrorAsync** — ServiceErrorEvent construction with instance identity and deduplication untested.
22. **MessageRetryBuffer internal retry loop** — Background retry loop (line 209+) that dequeues, re-publishes, handles permanent failures is untested.
23. **MessagingServicePlugin.ConfigureServices DI registration** — InMemory vs RabbitMQ mode selection, all factory registrations untested.
24. **TappedMessageEnvelope IBannouEvent constructor** — Constructor overload (lines 103-130) untested.

---

## Task #3: L0 State — 40 gaps [PENDING]

1. **InMemoryStateStore.SaveBulkAsync** — Bulk save with ETags/TTL untested directly.
2. **InMemoryStateStore.ExistsBulkAsync** — No direct test.
3. **InMemoryStateStore.DeleteBulkAsync** — No direct test.
4. **RedisStateStore.SaveAsync transaction failure path** — Retry/fallback logic (lines 142-213) minimally tested.
5. **RedisStateStore.SaveBulkAsync** — Error handling within bulk pipeline untested.
6. **RedisStateStore.SortedSetRangeByScoreAsync Exclude parameters** — Boundary exclusion (Both/Start/Stop/None) untested.
7. **RedisStateStore.HashSetManyAsync** — Batch hash set untested at Redis level.
8. **RedisStateStore.RefreshSetTtlAsync** — TTL refresh for sets (lines 856-891) untested.
9. **RedisStateStore.RefreshHashTtlAsync** — TTL refresh for hashes untested.
10. **RedisStateStore.DeleteHashAsync** — Hash deletion untested.
11. **RedisStateStore.DecrementAsync** — Decrement operation (lines 1299-1338) untested.
12. **RedisStateStore.SetCounterAsync** — Counter set (lines 1379-1416) untested.
13. **RedisStateStore.DeleteCounterAsync** — Counter deletion (lines 1417-1450) untested.
14. **RedisSearchStateStore.CreateIndexAsync** — (lines 510-597) zero coverage.
15. **RedisSearchStateStore.DropIndexAsync** — (lines 598-618) zero coverage.
16. **RedisSearchStateStore.SearchAsync** — (lines 619-718) zero coverage.
17. **RedisSearchStateStore.SuggestAsync** — (lines 719-757) zero coverage.
18. **RedisSearchStateStore.GetIndexInfoAsync** — (lines 758-802) zero coverage.
19. **RedisSearchStateStore.ListIndexesAsync** — (lines 803-825) zero coverage.
20. **RedisSearchStateStore base IStateStore operations** — Get/Save/Delete/Exists/Bulk all untested.
21. **RedisSearchStateStore ICacheableStateStore operations** — All set/sorted set/counter/hash operations untested.
22. **MySqlStateStore.QueryAsync** — LINQ query with predicate translation (lines 436-546) untested.
23. **MySqlStateStore.QueryPagedAsync** — Paged query (lines 547-727) untested.
24. **MySqlStateStore.CountAsync** — Count method (lines 728-821) untested.
25. **MySqlStateStore.JsonQueryAsync** — JSON path query (lines 822-868) untested.
26. **MySqlStateStore.JsonQueryPagedAsync** — Paged JSON query (lines 869-954) untested.
27. **MySqlStateStore.JsonCountAsync** — JSON count (lines 955-979) untested.
28. **MySqlStateStore.JsonDistinctAsync** — Distinct values (lines 980-1011) untested.
29. **MySqlStateStore.JsonAggregateAsync** — Aggregation (lines 1012+) untested.
30. **MySqlStateStore.DeleteBulkAsync** — Bulk delete (line 415) untested.
31. **StateStoreFactory.CreateSearchIndexesAsync** — Search index auto-creation (lines 861-912) untested.
32. **StateStoreFactory.GetKeyCountAsync MySQL/SQLite/Redis paths** — Only InMemory tested.
33. **StateStoreFactory.GetStoreInternal telemetry wrapping** — Most-specific interface wrapping logic (lines 536-571) untested.
34. **StateStoreFactory.GetStore sync-over-async warning path** — Warning path (lines 397-419) untested.
35. **StateStoreFactory.EnsureInitializedAsync MySQL retry loop** — Retry loop (lines 300-348) untested.
36. **StateStoreFactory.EnsureInitializedAsync SQLite mode** — SQLite init path (lines 249-276) untested.
37. **StateStoreFactory.DisposeAsync** — Redis disposal path untested.
38. **StateStoreFactory.ErrorPublisher deduplication** — Deduplication skip path (lines 182-229) untested.
39. **RedisDistributedLockProvider.AcquireRedisLockAsync** — Redis lock acquisition (lines 148-181) untested; only in-memory tested.
40. **RedisLockResponse.DisposeAsync / RedisDistributedLockProvider.DisposeAsync / RedisOperations all methods** — Thin Redis wrappers error handling and logging untested.

---

## Task #4: L0 Telemetry — 12 gaps [PENDING]

1. **TelemetryProvider.WrapQueryableStateStore()** — Public method creating InstrumentedQueryableStateStore has no test.
2. **TelemetryProvider.WrapSearchableStateStore()** — Public method creating InstrumentedSearchableStateStore has no test.
3. **TelemetryProvider.WrapJsonQueryableStateStore()** — Public method creating InstrumentedJsonQueryableStateStore has no test.
4. **TelemetryProvider.WrapCacheableStateStore()** — Public method creating InstrumentedCacheableStateStore has no test.
5. **InstrumentedStateStore.GetWithETagAsync()** — Instrumented wrapper for optimistic concurrency reads untested.
6. **InstrumentedStateStore.TrySaveAsync()** — Instrumented wrapper for optimistic concurrency writes untested.
7. **InstrumentedStateStore.SaveBulkAsync()** — Instrumented wrapper for bulk save untested.
8. **InstrumentedStateStore.ExistsBulkAsync()** — Instrumented wrapper for bulk existence checks untested.
9. **InstrumentedStateStore.DeleteBulkAsync()** — Instrumented wrapper for bulk delete untested.
10. **InstrumentedCacheableStateStore — All Counter operations** — IncrementCounterAsync, DecrementCounterAsync, GetCounterAsync, SetCounterAsync, DeleteCounterAsync all have zero tests.
11. **InstrumentedCacheableStateStore — All Hash operations** — HashGetAsync, HashSetAsync, HashSetManyAsync, HashDeleteAsync, HashExistsAsync, HashIncrementAsync, HashGetAllAsync, HashCountAsync, DeleteHashAsync, RefreshHashTtlAsync all have zero tests.
12. **TelemetryServicePlugin — Plugin lifecycle methods** — ConfigureServices(), ConfigureOpenTelemetry(), ConfigureApplication(), OnInitializeAsync(), OnStartAsync(), OnRunningAsync(), OnShutdownAsync() all untested.

---

## Task #5: L1 Account — 5 gaps [PENDING]

1. **AccountService.ListAccountsAsync() MaxPageSize clamping** — No test verifies that PageSize exceeding MaxPageSize gets clamped (line 75).
2. **AccountService.ListAccountsWithProviderFilterAsync()** — Private method (lines 201-278) with paged query logic, provider filtering, in-memory join, pagination handling is entirely unexercised — no test calls ListAccountsAsync with a provider filter.
3. **AccountService.PublishErrorEventAsync()** — Private error event publishing helper called from multiple methods is never verified in any test.
4. **AccountService.ConvertJsonElement()** — Recursive helper converting JsonElement values; recursive array and nested object paths not directly verified.
5. **AccountServicePlugin** — Plugin registration not tested.

---

## Task #6: L1 Auth — 27 gaps [PENDING] (OAuth exchange excluded)

1. **AuthService.GetRevocationListAsync()** — Public API method has zero tests.
2. **AuthService.TerminateSessionAsync()** — Public API method has zero tests.
3. **AuthService.LogoutAsync() successful path** — Only empty-JWT rejection tested; successful logout flow untested.
4. **AuthService.GetSessionsAsync() successful path** — Only empty-JWT rejection tested; successful path untested.
5. **AuthService.InvalidateAccountSessionsAsync()** — Private method internal logic not directly tested.
6. **AuthService.PropagateRoleChangesAsync()** — Internal session-iteration and per-session update logic not directly tested.
7. **AuthService.PropagateEmailChangeAsync()** — Internal session-iteration and per-session email update logic not directly tested.
8. **AuthService.PublishClientEventToAccountSessionsAsync()** — WebSocket push to all account sessions untested.
9. **AuthService event publishing private methods** — PublishLoginSuccessfulEventAsync, PublishLoginFailedEventAsync, PublishRegistrationSuccessfulEventAsync, PublishOAuthLoginSuccessfulEventAsync, PublishSteamLoginSuccessfulEventAsync, PublishPasswordResetSuccessfulEventAsync, PublishMfaEnabledEventAsync, PublishMfaDisabledEventAsync, PublishMfaVerifiedEventAsync, PublishMfaFailedEventAsync — none verified.
10. **AuthController.InitOAuth()** — Manual redirect controller untested.
11. **OAuthProviderService.ExchangeDiscordCodeAsync()** — Full OAuth code exchange flow untested.
12. **OAuthProviderService.ExchangeGoogleCodeAsync()** — Full OAuth code exchange flow untested.
13. **OAuthProviderService.ExchangeTwitchCodeAsync()** — Full OAuth code exchange flow untested.
14. **OAuthProviderService.CleanupOAuthLinksForAccountAsync()** — Cleanup logic (empty links, successful cleanup, error handling) not dedicated-tested.
15. **OAuthProviderService.GetEffectiveRedirectUri()** — ServiceDomain derivation path not explicitly tested.
16. **OAuthProviderService.AddToAccountOAuthLinksIndexAsync()** — Reverse index maintenance untested.
17. **OAuthProviderService.EnsureAuthMethodSyncedAsync()** — Exception handling path not tested beyond mock .Verify().
18. **EdgeRevocationService.RetryFailedPushesAsync()** — Retry mechanism for failed edge pushes untested.
19. **EdgeRevocationService.RevokeAccountAsync() provider failure** — No test for when a provider fails during account revocation.
20. **EdgeRevocationService.PushToProvidersAsync() timeout path** — Timeout/OperationCanceledException path untested.
21. **SessionService.GetAccountSessionsAsync() error handling** — Outer catch block publishing error event untested.
22. **SessionService.AddSessionToAccountIndexAsync() error handling** — Error path untested.
23. **SessionService.InvalidateAllSessionsForAccountAsync() edge revocation** — Branch when IsEnabled=true for JTI extraction/edge revocation untested.
24. **TokenService.ValidateTokenAsync() session data corruption** — Null Roles/Authorizations path (lines 251-265) untested.
25. **TokenService.ValidateTokenAsync() session expiration** — Expired session path (lines 244-249) untested.
26. **TokenService.ValidateTokenAsync() last activity update** — LastActiveAt update (lines 268-270) untested.
27. **AuthServicePlugin.ConfigureServices() email provider registration** — Sendgrid/SMTP/SES/Console provider selection and validation logic untested.

---

## Task #7: L1 Chat — 8 gaps [PENDING]

1. **ChangeParticipantRoleAsync** — Zero test coverage. Multiple validation branches (caller must be owner, cannot change own role, cannot promote to Owner, target must exist) and business logic (update, save, publish events) all untested.
2. **UnmuteParticipantAsync** — Zero direct test coverage. Auto-unmute side effect in SendMessageAsync is tested, but the endpoint itself (successful unmute, target not in room, caller lacks permission, target not muted, event publication) is not.
3. **ChatServicePlugin** — Zero test coverage. ConfigureServices (background worker registration) and OnStartedAsync (3 built-in room type registrations) untested.
4. **ExecuteContractRoomActionAsync error paths** — Error paths within the private method (e.g., archive fails, deletion fails mid-processing during contract events) untested.
5. **FindRoomsByContractIdAsync safety cap** — Safety cap branch (line 192-198) where allRooms.Count >= maxResults triggers warning and breaks early is untested.
6. **CleanupIdleRoomsAsync recently-active room preservation** — No test verifies that rooms below idle timeout threshold are NOT cleaned up.
7. **JoinRoomAsync archived room rejection** — No test verifying Forbidden for archived room (ChatRoomStatus.Archived).
8. **SendMessageAsync custom format success** — No test for successful custom format message send with valid payload (only error paths tested).

---

## Task #9: L1 Contract — 18 gaps [PENDING]

1. **QueryContractInstancesAsync** — Zero test coverage for this public API method (line 826).
2. **ContractServicePlugin** — Zero test coverage for DI registration.
3. **DeleteContractTemplateAsync non-deprecated rejection** — No test verifying BadRequest when deleting a non-deprecated template (T31 Category A).
4. **LockContractAsync non-existent contract** — No test for NotFound on non-existent contract ID.
5. **UnlockContractAsync non-existent contract** — No test for NotFound on non-existent contract ID.
6. **TransferContractPartyAsync non-existent contract / wrong guardian** — No test for these error paths.
7. **SetContractTemplateValuesAsync non-existent contract** — No test for NotFound.
8. **CheckAssetRequirementsAsync non-existent contract** — No test for NotFound.
9. **ExecuteContractAsync distribution failure handling** — No test for when a distribution clause execution fails and failure is recorded in ExecutionDistributions.
10. **CompleteMilestoneAsync non-existent contract** — No test for NotFound.
11. **FailMilestoneAsync non-existent contract** — No test for NotFound.
12. **CureBreachAsync already-cured breach** — No test for attempting to cure an already-cured breach.
13. **ReportBreachAsync breach threshold triggering termination** — CheckBreachThresholdAsync path untested.
14. **ConsentToContractAsync non-existent contract** — No test for NotFound.
15. **TerminateContractInstanceAsync already-terminated contract** — No test for attempting to terminate an already-terminated/fulfilled contract.
16. **ContractServiceModels.ClauseDefinition** — GetProperty and GetArray methods with JSON parsing logic have zero tests.
17. **UpdateContractTemplateAsync lock failure** — No test for distributed lock acquisition failure (Conflict).
18. **ContractExpirationService payment schedule check flow** — Full payment-due event publishing path untested.

---

## Task #10: L1 Permission — 16 gaps [PENDING]

1. **IPermissionRegistry.RegisterServiceAsync (explicit interface)** — Conversion logic and InvalidOperationException path (lines 913-942) untested.
2. **RegisterServicePermissionsAsync idempotent skip path** — Three-condition skip (hash match + service already registered) (lines 223-228) untested.
3. **RegisterServicePermissionsAsync with null Permissions** — Warning-and-continue branch (lines 282-284) untested.
4. **UpdateSessionStateAsync lock failure** — Conflict return on lock failure (lines 361-365) untested.
5. **UpdateSessionRoleAsync lock failure** — Conflict return on lock failure (lines 405-410) untested.
6. **RecompileSessionPermissionsAsync exception/catch** — Error event publishing on compilation failure (lines 744-754) untested.
7. **PublishCapabilityUpdateAsync exception path** — Error event on PublishToSessionAsync exception (lines 813-817) untested.
8. **PublishCapabilityUpdateAsync publish returns false** — Warning log path when publish returns false (lines 809-811) untested.
9. **GetRegisteredServicesAsync** — Public API method (lines 839-904) has zero tests.
10. **DetermineHighestPriorityRole fallback** — Case where roles contain values NOT in the configured hierarchy (line 1123) untested.
11. **ComputePermissionDataHash** — No dedicated test for hash determinism or different-input-different-hash.
12. **GetSessionDataStateOptions zero TTL** — Path returning null when SessionDataTtlSeconds <= 0 (lines 1091-1097) untested.
13. **HandleSessionConnectedAsync invalid authorization format** — Malformed authorizations via direct DI call path (lines 979-993) untested.
14. **HandleSessionUpdatedAsync exception/catch** — Outer catch publishing error event (lines 118-122 in Events file) untested.
15. **PermissionSessionActivityListener.OnReconnectedAsync exception** — Behavior when RecompileForReconnectionAsync fails untested.
16. **PermissionSessionActivityListener.AlignSessionTtlAsync Redis failure** — SetKeyExpiryAsync failure on one or both keys untested.

---

## Task #11: L1 Resource — 20 gaps [PENDING]

1. **RegisterReferenceAsync duplicate/idempotent** — No test for same source registered twice (AlreadyRegistered=true, no count increment).
2. **UnregisterReferenceAsync not-registered source** — No test for WasRegistered=false when unregistering never-registered source.
3. **CheckReferencesAsync zero refs with no grace period record** — No test when grace store returns null.
4. **ExecuteCleanupAsync DryRun with eligible state** — No test for dry run where resource IS eligible (no refs, grace passed).
5. **ExecuteCleanupAsync re-validation under lock** — Race condition protection (refs added between pre-check and lock) untested.
6. **ExecuteCleanupAsync published events** — No test verifies cleanup completion event is published.
7. **ExecuteCleanupAsync DETACH callback behavior** — DETACH semantic (proceed despite active references) lacks focused test.
8. **ExecuteCleanupAsync callback failure with ALL_REQUIRED** — Cleanup abort on API 500 under ALL_REQUIRED policy untested.
9. **ExecuteCleanupAsync callback failure with BEST_EFFORT** — Partial callback failure under BEST_EFFORT policy untested.
10. **ExecuteCompressAsync with DeleteSourceData=true** — Interaction between compress and cleanup phases when source data deletion requested is shallow.
11. **ExecuteDecompressAsync missing callback** — Archive entry with no matching decompress callback untested.
12. **ExecuteDecompressAsync lock failure** — Lock contention during decompression untested.
13. **ExecuteSnapshotAsync with filter** — Snapshot creation with source type filter untested.
14. **GetSnapshotAsync with filter** — Retrieval with source type filter reducing entries untested.
15. **CompressJsonData static helper** — Edge cases (empty string, very large data) not covered.
16. **ComputeChecksum static helper** — SHA256 correctness not directly verified.
17. **MaintainCallbackIndexAsync / MaintainCompressCallbackIndexAsync** — Index update when callbacks removed (stale entries) untested.
18. **ListCleanupCallbacksAsync with no filter** — Listing all callbacks across all resource types (iterating master index) untested.
19. **Error event publishing for callback failures** — TryPublishErrorAsync not systematically verified across all error paths.
20. **ResourceServiceModels property-level tests** — GracePeriodRecord, CleanupCallbackDefinition, CompressCallbackDefinition lack property/construction tests.
