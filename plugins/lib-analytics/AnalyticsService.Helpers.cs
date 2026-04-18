using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BeyondImmersion.BannouService.Analytics;

// =============================================================================
// AnalyticsService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AnalyticsService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AnalyticsService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAnalyticsService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AnalyticsService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for AnalyticsService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AnalyticsService
{
    private async Task<bool> EnsureSummaryStoreRedisAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.EnsureSummaryStoreRedisAsync");
        if (_summaryStoreIsRedis)
        {
            return true;
        }

        var message = "Analytics summary store must use Redis to support buffered ingestion";
        _logger.LogError(
            "{Message} (StoreName: {StoreName})",
            message,
            StateStoreDefinitions.AnalyticsSummary);
        await _messageBus.TryPublishErrorAsync(
            "analytics",
            "EnsureSummaryStoreRedis",
            "analytics_summary_store_invalid",
            message,
            dependency: "state",
            endpoint: "state:summary",
            details: $"store:{StateStoreDefinitions.AnalyticsSummary}",
            stack: null,
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task<Guid?> ResolveGameServiceIdAsync(string gameType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.ResolveGameServiceIdAsync");
        if (string.IsNullOrWhiteSpace(gameType))
        {
            var message = "Game type is required to resolve game service ID";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "game_type_missing",
                message,
                dependency: null,
                endpoint: "event:game-session",
                details: null,
                stack: null,
                cancellationToken: cancellationToken);
            return null;
        }

        var stubName = gameType.Trim().ToLowerInvariant();
        var cacheOptions = BuildResolutionCacheOptions();
        if (cacheOptions != null)
        {
            var cacheKey = BuildGameServiceCacheKey(stubName);
            var cached = await _gameServiceCacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return cached.ServiceId;
            }
        }

        try
        {
            var response = await _gameServiceClient.GetServiceAsync(new GetServiceRequest
            {
                StubName = stubName
            }, cancellationToken);

            if (response == null)
            {
                // Per IMPLEMENTATION TENETS: Not found (404) is expected, log at Warning, do NOT emit error events
                _logger.LogWarning("Game service lookup returned no data for game type {GameType}", gameType);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheKey = BuildGameServiceCacheKey(stubName);
                await _gameServiceCacheStore.SaveAsync(cacheKey, new GameServiceCacheEntry
                {
                    ServiceId = response.ServiceId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return response.ServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to resolve game service for game type {GameType}: {Status}", gameType, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "ApiException",
                ex.Message,
                dependency: "game-service",
                endpoint: "game-service/get",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for game type {GameType}", gameType);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "unexpected_exception",
                ex.Message,
                dependency: "game-service",
                endpoint: "service:game-service/get",
                details: $"gameType:{gameType}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task<Guid?> ResolveGameServiceIdForSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.ResolveGameServiceIdForSessionAsync");
        try
        {
            var mappingKey = BuildSessionMappingKey(sessionId);
            var mapping = await _sessionMappingStore.GetAsync(mappingKey, cancellationToken);
            if (mapping != null)
            {
                return mapping.GameServiceId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read game session mapping for session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "session_mapping_lookup_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"sessionId:{sessionId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }

        try
        {
            var session = await _gameSessionClient.GetGameSessionAsync(new GetGameSessionRequest
            {
                SessionId = sessionId
            }, cancellationToken);

            if (session == null)
            {
                var message = "Game session lookup returned no data";
                _logger.LogError("{Message} (SessionId: {SessionId})", message, sessionId);
                await _messageBus.TryPublishErrorAsync(
                    "analytics",
                    "ResolveGameServiceIdForSession",
                    "game_session_lookup_empty",
                    message,
                    dependency: "game-session",
                    endpoint: "service:game-session/get",
                    details: $"sessionId:{sessionId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                return null;
            }

            var gameType = session.GameType.ToString().ToLowerInvariant();
            var gameServiceId = await ResolveGameServiceIdAsync(gameType, cancellationToken);
            if (gameServiceId.HasValue)
            {
                await SaveGameSessionMappingAsync(sessionId, gameType, gameServiceId.Value, cancellationToken);
            }

            return gameServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to resolve game session {SessionId} for analytics event: {Status}", sessionId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "ApiException",
                ex.Message,
                dependency: "game-session",
                endpoint: "game-session/get",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "unexpected_exception",
                ex.Message,
                dependency: "game-session",
                endpoint: "service:game-session/get",
                details: $"sessionId:{sessionId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task SaveGameSessionMappingAsync(
        Guid sessionId,
        string gameType,
        Guid gameServiceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.SaveGameSessionMappingAsync");
        var mappingKey = BuildSessionMappingKey(sessionId);
        var cacheOptions = BuildSessionMappingCacheOptions();
        await _sessionMappingStore.SaveAsync(mappingKey, new GameSessionMappingData
        {
            SessionId = sessionId,
            GameType = gameType,
            GameServiceId = gameServiceId,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cacheOptions, cancellationToken);
    }

    private async Task RemoveGameSessionMappingAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.RemoveGameSessionMappingAsync");
        var mappingKey = BuildSessionMappingKey(sessionId);
        await _sessionMappingStore.DeleteAsync(mappingKey, cancellationToken);
    }

    /// <summary>
    /// Resolves the game service ID for a given realm by looking up the realm via client.
    /// Results are cached using the standard summary cache TTL.
    /// </summary>
    private async Task<Guid?> ResolveGameServiceIdForRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.ResolveGameServiceIdForRealmAsync");
        var cacheOptions = BuildResolutionCacheOptions();
        if (cacheOptions != null)
        {
            var cacheKey = BuildRealmGameServiceCacheKey(realmId);
            var cached = await _realmGameServiceCacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return cached.GameServiceId;
            }
        }

        try
        {
            var realm = await _realmClient.GetRealmAsync(new GetRealmRequest
            {
                RealmId = realmId
            }, cancellationToken);

            if (realm == null)
            {
                // Per IMPLEMENTATION TENETS: Not found (404) is expected, log at Warning, do NOT emit error events
                _logger.LogWarning("Realm lookup returned no data for realm {RealmId}", realmId);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheKey = BuildRealmGameServiceCacheKey(realmId);
                await _realmGameServiceCacheStore.SaveAsync(cacheKey, new RealmGameServiceCacheEntry
                {
                    GameServiceId = realm.GameServiceId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return realm.GameServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to resolve game service for realm {RealmId}: {Status}", realmId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForRealm",
                "ApiException",
                ex.Message,
                dependency: "realm",
                endpoint: "realm/get",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for realm {RealmId}", realmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForRealm",
                "unexpected_exception",
                ex.Message,
                dependency: "realm",
                endpoint: "service:realm/get",
                details: $"realmId:{realmId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Resolves the game service ID for a given character by looking up the character's realm,
    /// then resolving the realm's game service ID.
    /// Results are cached using the standard summary cache TTL.
    /// </summary>
    private async Task<Guid?> ResolveGameServiceIdForCharacterAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.ResolveGameServiceIdForCharacterAsync");
        var cacheOptions = BuildResolutionCacheOptions();
        if (cacheOptions != null)
        {
            var cacheKey = BuildCharacterRealmCacheKey(characterId);
            var cached = await _characterRealmCacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return await ResolveGameServiceIdForRealmAsync(cached.RealmId, cancellationToken);
            }
        }

        try
        {
            var character = await _characterClient.GetCharacterAsync(new GetCharacterRequest
            {
                CharacterId = characterId
            }, cancellationToken);

            if (character == null)
            {
                // Per IMPLEMENTATION TENETS: Not found (404) is expected, log at Warning, do NOT emit error events
                _logger.LogWarning("Character lookup returned no data for character {CharacterId}", characterId);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheKey = BuildCharacterRealmCacheKey(characterId);
                await _characterRealmCacheStore.SaveAsync(cacheKey, new CharacterRealmCacheEntry
                {
                    RealmId = character.RealmId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return await ResolveGameServiceIdForRealmAsync(character.RealmId, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to resolve game service for character {CharacterId}: {Status}", characterId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForCharacter",
                "ApiException",
                ex.Message,
                dependency: "character",
                endpoint: "character/get",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for character {CharacterId}", characterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: "character",
                endpoint: "service:character/get",
                details: $"characterId:{characterId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task<bool> BufferAnalyticsEventAsync(
        BufferedAnalyticsEvent bufferedEvent,
        CancellationToken cancellationToken,
        bool flushAfterEnqueue = true)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.BufferAnalyticsEventAsync");
        if (!await EnsureSummaryStoreRedisAsync(cancellationToken))
        {
            return false;
        }

        string? eventKey = null;

        try
        {
            eventKey = BuildEventBufferEntryKey(bufferedEvent.EventId);

            await _eventBufferStore.SaveAsync(eventKey, bufferedEvent, options: null, cancellationToken);
            await _eventBufferIndexStore.SortedSetAddAsync(
                EVENT_BUFFER_INDEX_KEY,
                eventKey,
                bufferedEvent.Timestamp.ToUnixTimeMilliseconds(),
                options: null,
                cancellationToken: cancellationToken);

            if (flushAfterEnqueue)
            {
                await FlushBufferedEventsIfNeededAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (eventKey != null)
                {
                    await _eventBufferStore.DeleteAsync(eventKey, cancellationToken);
                    await _eventBufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, eventKey, cancellationToken);
                }
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException,
                    "Failed to clean up buffered analytics event {EventId} after error",
                    bufferedEvent.EventId);
            }

            _logger.LogError(ex,
                "Failed to buffer analytics event {EventId} for {EntityType}:{EntityId}",
                bufferedEvent.EventId,
                bufferedEvent.EntityType,
                bufferedEvent.EntityId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "BufferAnalyticsEvent",
                "analytics_event_buffer_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"eventId:{bufferedEvent.EventId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    private async Task FlushBufferedEventsIfNeededAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.FlushBufferedEventsIfNeededAsync");
        if (!await EnsureSummaryStoreRedisAsync(cancellationToken))
        {
            return;
        }

        var bufferSize = _configuration.EventBufferSize;
        if (bufferSize <= 0)
        {
            var message = "Analytics event buffer size must be greater than zero";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_size_invalid",
                message,
                dependency: null,
                endpoint: "config:analytics",
                details: $"bufferSize:{bufferSize}",
                stack: null,
                cancellationToken: cancellationToken);
            return;
        }

        var flushIntervalSeconds = _configuration.EventBufferFlushIntervalSeconds;
        if (flushIntervalSeconds < 0)
        {
            var message = "Analytics event buffer flush interval must be non-negative";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_interval_invalid",
                message,
                dependency: null,
                endpoint: "config:analytics",
                details: $"flushIntervalSeconds:{flushIntervalSeconds}",
                stack: null,
                cancellationToken: cancellationToken);
            return;
        }

        var bufferCount = await _eventBufferIndexStore.SortedSetCountAsync(EVENT_BUFFER_INDEX_KEY, cancellationToken);
        if (bufferCount == 0)
        {
            return;
        }

        var shouldFlush = bufferCount >= bufferSize;
        if (!shouldFlush && flushIntervalSeconds > 0)
        {
            var oldest = await _eventBufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                0,
                descending: false,
                cancellationToken);
            if (oldest.Count == 0)
            {
                return;
            }

            var oldestTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(oldest[0].score));
            shouldFlush = (DateTimeOffset.UtcNow - oldestTimestamp).TotalSeconds >= flushIntervalSeconds;
        }

        if (!shouldFlush)
        {
            return;
        }

        var lockExpirySeconds = Math.Max(_configuration.EventBufferLockExpiryBaseSeconds, flushIntervalSeconds > 0 ? flushIntervalSeconds * 2 : _configuration.EventBufferLockExpiryBaseSeconds);
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AnalyticsSummary,
            BUFFER_LOCK_RESOURCE,
            Guid.NewGuid().ToString(),
            lockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            return;
        }

        bufferCount = await _eventBufferIndexStore.SortedSetCountAsync(EVENT_BUFFER_INDEX_KEY, cancellationToken);
        if (bufferCount == 0)
        {
            return;
        }

        shouldFlush = bufferCount >= bufferSize;
        if (!shouldFlush && flushIntervalSeconds > 0)
        {
            var oldest = await _eventBufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                0,
                descending: false,
                cancellationToken);
            if (oldest.Count == 0)
            {
                return;
            }

            var oldestTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(oldest[0].score));
            shouldFlush = (DateTimeOffset.UtcNow - oldestTimestamp).TotalSeconds >= flushIntervalSeconds;
        }

        if (!shouldFlush)
        {
            return;
        }

        try
        {
            await FlushBufferedEventsBatchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing analytics event buffer");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_flush_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    private async Task FlushBufferedEventsBatchAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.FlushBufferedEventsBatchAsync");
        var batchSize = Math.Max(1, _configuration.EventBufferSize);

        while (true)
        {
            var entries = await _eventBufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                batchSize - 1,
                descending: false,
                cancellationToken: cancellationToken);

            if (entries.Count == 0)
            {
                return;
            }

            var eventKeys = entries.Select(e => e.member).ToList();
            var bufferedEvents = await _eventBufferStore.GetBulkAsync(eventKeys, cancellationToken);
            var envelopes = new List<(string key, BufferedAnalyticsEvent evt)>();

            foreach (var key in eventKeys)
            {
                if (bufferedEvents.TryGetValue(key, out var bufferedEvent))
                {
                    envelopes.Add((key, bufferedEvent));
                }
                else
                {
                    await _eventBufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, key, cancellationToken);
                }
            }

            if (envelopes.Count == 0)
            {
                if (entries.Count < batchSize)
                {
                    return;
                }

                continue;
            }

            var eventsByEntity = new Dictionary<string, List<(string key, BufferedAnalyticsEvent evt)>>();
            foreach (var envelope in envelopes)
            {
                var entityKey = BuildEntityKey(envelope.evt.ServiceType, envelope.evt.ServiceId, envelope.evt.EntityType, envelope.evt.EntityId);
                if (!eventsByEntity.TryGetValue(entityKey, out var list))
                {
                    list = new List<(string key, BufferedAnalyticsEvent evt)>();
                    eventsByEntity[entityKey] = list;
                }
                list.Add(envelope);
            }

            foreach (var kvp in eventsByEntity)
            {
                var entityKey = kvp.Key;
                var entityEvents = kvp.Value;
                var (summary, summaryEtag) = await _summaryDataStore.GetWithETagAsync(entityKey, cancellationToken);

                if (summary == null)
                {
                    var firstEvent = entityEvents[0].evt;
                    summary = new EntitySummaryData
                    {
                        EntityId = firstEvent.EntityId,
                        EntityType = firstEvent.EntityType,
                        ServiceType = firstEvent.ServiceType,
                        ServiceId = firstEvent.ServiceId,
                        FirstEventAt = firstEvent.Timestamp,
                        EventCounts = new Dictionary<string, long>(),
                        Aggregates = new Dictionary<string, double>()
                    };
                }
                else
                {
                    summary.EventCounts ??= new Dictionary<string, long>();
                    summary.Aggregates ??= new Dictionary<string, double>();
                }

                var scoreEvents = new List<AnalyticsScoreUpdatedEvent>();
                var milestoneChecks = new List<(AnalyticsServiceType serviceType, string serviceId, Guid entityId, EntityType entityType, string scoreType, double previousValue, double newValue)>();

                foreach (var envelope in entityEvents)
                {
                    var bufferedEvent = envelope.evt;
                    summary.EventCounts[bufferedEvent.EventType] = summary.EventCounts.GetValueOrDefault(bufferedEvent.EventType) + 1;
                    summary.TotalEvents++;

                    if (summary.FirstEventAt == default || bufferedEvent.Timestamp < summary.FirstEventAt)
                    {
                        summary.FirstEventAt = bufferedEvent.Timestamp;
                    }
                    if (summary.LastEventAt == default || bufferedEvent.Timestamp > summary.LastEventAt)
                    {
                        summary.LastEventAt = bufferedEvent.Timestamp;
                    }

                    if (bufferedEvent.Value.HasValue)
                    {
                        var hasPrevious = summary.Aggregates.TryGetValue(bufferedEvent.EventType, out var previousAggregate);
                        var previousValue = hasPrevious ? previousAggregate : 0.0;
                        var newValue = previousValue + bufferedEvent.Value.Value;
                        summary.Aggregates[bufferedEvent.EventType] = newValue;

                        scoreEvents.Add(new AnalyticsScoreUpdatedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            ServiceType = bufferedEvent.ServiceType,
                            ServiceId = bufferedEvent.ServiceId,
                            EntityId = bufferedEvent.EntityId,
                            EntityType = bufferedEvent.EntityType,
                            ScoreType = bufferedEvent.EventType,
                            PreviousValue = hasPrevious ? previousValue : null,
                            NewValue = newValue,
                            Delta = bufferedEvent.Value.Value,
                            SessionId = bufferedEvent.SessionId
                        });
                        milestoneChecks.Add((bufferedEvent.ServiceType, bufferedEvent.ServiceId, bufferedEvent.EntityId, bufferedEvent.EntityType, bufferedEvent.EventType, previousValue, newValue));
                    }
                }

                var summarySaved = false;
                try
                {
                    // GetWithETagAsync returns null ETag for new entities; TrySaveAsync expects
                    // empty string for create operations - coalesce satisfies compiler's nullable analysis
                    var newSummaryEtag = await _summaryDataStore.TrySaveAsync(entityKey, summary, summaryEtag ?? string.Empty, cancellationToken: cancellationToken);
                    if (newSummaryEtag == null)
                    {
                        _logger.LogWarning("Concurrent modification detected for analytics summary {EntityKey}, skipping batch", entityKey);
                        continue;
                    }
                    summarySaved = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving analytics summary for {EntityKey}", entityKey);
                    await _messageBus.TryPublishErrorAsync(
                        "analytics",
                        "FlushBufferedEventsBatch",
                        "analytics_summary_save_failed",
                        ex.Message,
                        dependency: "state",
                        endpoint: "state:summary",
                        details: $"entityKey:{entityKey}",
                        stack: ex.StackTrace,
                        cancellationToken: cancellationToken);
                }

                if (!summarySaved)
                {
                    continue;
                }

                foreach (var scoreEvent in scoreEvents)
                {
                    await _messageBus.PublishAnalyticsScoreUpdatedAsync(scoreEvent, cancellationToken);

                    // Per-score metric: enables rate/increase queries in Prometheus (e.g., kills per hour, gold per day)
                    _telemetryProvider.RecordCounter(
                        TelemetryComponents.Analytics,
                        TelemetryMetrics.AnalyticsScoreProcessed,
                        (long)scoreEvent.Delta,
                        new KeyValuePair<string, object?>("service_type", scoreEvent.ServiceType.ToString()),
                        new KeyValuePair<string, object?>("service_id", scoreEvent.ServiceId),
                        new KeyValuePair<string, object?>("entity_type", scoreEvent.EntityType.ToString()),
                        new KeyValuePair<string, object?>("score_type", scoreEvent.ScoreType));
                }

                foreach (var milestone in milestoneChecks)
                {
                    await CheckAndPublishMilestoneAsync(
                        milestone.serviceType,
                        milestone.serviceId,
                        milestone.entityId,
                        milestone.entityType,
                        milestone.scoreType,
                        milestone.previousValue,
                        milestone.newValue,
                        cancellationToken);
                }

                // Per-entity batch throughput metric: enables event ingestion rate monitoring
                _telemetryProvider.RecordCounter(
                    TelemetryComponents.Analytics,
                    TelemetryMetrics.AnalyticsEventsProcessed,
                    entityEvents.Count,
                    new KeyValuePair<string, object?>("service_type", summary.ServiceType.ToString()),
                    new KeyValuePair<string, object?>("service_id", summary.ServiceId));

                foreach (var envelope in entityEvents)
                {
                    await _eventBufferStore.DeleteAsync(envelope.key, cancellationToken);
                    await _eventBufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, envelope.key, cancellationToken);
                }
            }

            if (entries.Count < batchSize)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Checks if a milestone has been crossed and publishes event if so.
    /// A milestone is considered crossed when previousValue was below the threshold
    /// and newValue is at or above it. This ensures each milestone is only triggered
    /// once when the entity first reaches it, not on subsequent updates.
    /// </summary>
    private async Task CheckAndPublishMilestoneAsync(
        AnalyticsServiceType serviceType,
        string serviceId,
        Guid entityId,
        EntityType entityType,
        string scoreType,
        double previousValue,
        double newValue,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.CheckAndPublishMilestoneAsync");
        foreach (var milestone in _milestoneThresholds)
        {
            // Check if we just crossed this milestone: was below, now at or above
            if (previousValue < milestone && newValue >= milestone)
            {
                var milestoneEvent = new AnalyticsMilestoneReachedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ServiceType = serviceType,
                    ServiceId = serviceId,
                    EntityId = entityId,
                    EntityType = entityType,
                    MilestoneType = scoreType,
                    MilestoneValue = milestone,
                    MilestoneName = $"{scoreType}_{milestone}"
                };
                await _messageBus.PublishAnalyticsMilestoneReachedAsync(milestoneEvent, cancellationToken);
            }
        }
    }
}
