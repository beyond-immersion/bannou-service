#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Distributed circuit breaker that shares state across all mesh instances.
/// Uses Redis for authoritative state with local cache for fast reads.
/// Event-backed cache invalidation ensures all instances stay synchronized.
/// </summary>
/// <remarks>
/// Design follows IMPLEMENTATION TENETS (Multi-Instance Safety):
/// - Redis stores authoritative state via Lua scripts for atomicity
/// - Local ConcurrentDictionary provides 0ms reads on hot path
/// - RabbitMQ pub/sub propagates state changes across instances
/// - Graceful degradation: returns Closed when Redis unavailable
/// </remarks>
public sealed class DistributedCircuitBreaker
{
    private readonly IRedisOperations? _redis;
    private readonly IMessageBus _messageBus;
    private readonly ILogger _logger;
    private readonly int _threshold;
    private readonly TimeSpan _resetTimeout;
    private readonly string _keyPrefix;

    /// <summary>
    /// Local cache for circuit state. Updated via events from other instances.
    /// Key: appId, Value: (State, OpenedAt, LastUpdated)
    /// </summary>
    private readonly ConcurrentDictionary<string, CircuitCacheEntry> _localCache = new();

    /// <summary>
    /// Creates a new DistributedCircuitBreaker.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for obtaining Redis operations.</param>
    /// <param name="messageBus">Message bus for publishing state change events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="threshold">Number of consecutive failures before opening circuit.</param>
    /// <param name="resetTimeout">Time to wait before allowing a probe request (HalfOpen transition).</param>
    public DistributedCircuitBreaker(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger logger,
        int threshold,
        TimeSpan resetTimeout)
    {
        _redis = stateStoreFactory.GetRedisOperations();
        _messageBus = messageBus;
        _logger = logger;
        _threshold = threshold;
        _resetTimeout = resetTimeout;
        _keyPrefix = "mesh:cb:";

        if (_redis == null)
        {
            _logger.LogWarning(
                "DistributedCircuitBreaker running without Redis (InMemory mode). " +
                "Circuit state will NOT be shared across instances.");
        }
    }

    /// <summary>
    /// Gets the current circuit state for an app-id.
    /// Checks local cache first, falls back to Redis.
    /// Auto-transitions Open→HalfOpen when reset timeout has elapsed.
    /// </summary>
    /// <param name="appId">The app-id to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current circuit state.</returns>
    public async Task<CircuitState> GetStateAsync(string appId, CancellationToken cancellationToken = default)
    {
        // Check local cache first
        if (_localCache.TryGetValue(appId, out var cached))
        {
            // If Open, check if we should transition to HalfOpen
            if (cached.State == CircuitState.Open && cached.OpenedAt.HasValue)
            {
                if (DateTimeOffset.UtcNow >= cached.OpenedAt.Value + _resetTimeout)
                {
                    // Time elapsed - update cache to HalfOpen and let Redis confirm
                    cached = cached with { State = CircuitState.HalfOpen };
                    _localCache[appId] = cached;
                }
            }

            return cached.State;
        }

        // No cache entry - query Redis
        if (_redis == null)
        {
            return CircuitState.Closed; // Graceful degradation
        }

        try
        {
            var key = GetRedisKey(appId);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var resetMs = (long)_resetTimeout.TotalMilliseconds;

            var result = await _redis.ScriptEvaluateAsync(
                MeshLuaScripts.GetCircuitState,
                new RedisKey[] { key },
                new RedisValue[] { nowMs, resetMs },
                cancellationToken);

            var json = result.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return CircuitState.Closed;
            }

            var parsed = ParseGetStateResult(json);

            // Update local cache
            _localCache[appId] = new CircuitCacheEntry(
                parsed.State,
                parsed.OpenedAt,
                DateTimeOffset.UtcNow);

            // If state changed during the query (Open→HalfOpen), publish event
            if (parsed.StateChanged)
            {
                await PublishStateChangeAsync(appId, parsed.State, null, parsed.Failures, parsed.OpenedAt, cancellationToken);
            }

            return parsed.State;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get circuit state from Redis for {AppId}, using local cache or Closed", appId);
            return _localCache.TryGetValue(appId, out var fallback) ? fallback.State : CircuitState.Closed;
        }
    }

    /// <summary>
    /// Records a successful invocation. Resets circuit to Closed state if needed.
    /// </summary>
    /// <param name="appId">The app-id that succeeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecordSuccessAsync(string appId, CancellationToken cancellationToken = default)
    {
        // Update local cache immediately
        var previousEntry = _localCache.TryGetValue(appId, out var cached) ? cached : null;
        var previousState = previousEntry?.State;

        _localCache[appId] = new CircuitCacheEntry(CircuitState.Closed, null, DateTimeOffset.UtcNow);

        if (_redis == null)
        {
            return; // No Redis, local-only operation
        }

        try
        {
            var key = GetRedisKey(appId);

            var result = await _redis.ScriptEvaluateAsync(
                MeshLuaScripts.RecordCircuitSuccess,
                new RedisKey[] { key },
                Array.Empty<RedisValue>(),
                cancellationToken);

            var json = result.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var parsed = ParseSuccessResult(json);

            // If state changed, publish event
            if (parsed.StateChanged)
            {
                await PublishStateChangeAsync(appId, CircuitState.Closed, parsed.PreviousState, 0, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record circuit success in Redis for {AppId}", appId);
        }
    }

    /// <summary>
    /// Records a failed invocation. Opens circuit when threshold is reached.
    /// </summary>
    /// <param name="appId">The app-id that failed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecordFailureAsync(string appId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var previousEntry = _localCache.TryGetValue(appId, out var cached) ? cached : null;
        var previousState = previousEntry?.State ?? CircuitState.Closed;

        if (_redis == null)
        {
            // Local-only: simple threshold check
            var failures = (previousEntry?.Failures ?? 0) + 1;
            var newState = failures >= _threshold ? CircuitState.Open : CircuitState.Closed;
            var openedAt = newState == CircuitState.Open ? now : (DateTimeOffset?)null;

            _localCache[appId] = new CircuitCacheEntry(newState, openedAt, now) { Failures = failures };
            return;
        }

        try
        {
            var key = GetRedisKey(appId);
            var nowMs = now.ToUnixTimeMilliseconds();
            var resetMs = (long)_resetTimeout.TotalMilliseconds;

            var result = await _redis.ScriptEvaluateAsync(
                MeshLuaScripts.RecordCircuitFailure,
                new RedisKey[] { key },
                new RedisValue[] { _threshold, nowMs, resetMs },
                cancellationToken);

            var json = result.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var parsed = ParseFailureResult(json);

            // Update local cache
            _localCache[appId] = new CircuitCacheEntry(parsed.State, parsed.OpenedAt, now) { Failures = parsed.Failures };

            // If state changed, publish event
            if (parsed.StateChanged)
            {
                await PublishStateChangeAsync(appId, parsed.State, previousState, parsed.Failures, parsed.OpenedAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record circuit failure in Redis for {AppId}", appId);
        }
    }

    /// <summary>
    /// Handles a circuit state change event from another instance.
    /// Updates the local cache to stay synchronized.
    /// </summary>
    /// <param name="evt">The state change event.</param>
    public void HandleStateChangeEvent(MeshCircuitStateChangedEvent evt)
    {
        if (string.IsNullOrEmpty(evt.AppId))
        {
            return;
        }

        // Update local cache with the new state
        _localCache[evt.AppId] = new CircuitCacheEntry(
            evt.NewState,
            evt.OpenedAt,
            evt.ChangedAt)
        {
            Failures = evt.ConsecutiveFailures
        };

        _logger.LogDebug(
            "Updated local circuit cache for {AppId}: {State} (from event)",
            evt.AppId, evt.NewState);
    }

    /// <summary>
    /// Clears local cache state for an app-id. Used for testing.
    /// </summary>
    /// <param name="appId">The app-id to clear.</param>
    public void ClearLocalCache(string appId)
    {
        _localCache.TryRemove(appId, out _);
    }

    /// <summary>
    /// Clears all local cache state. Used for testing.
    /// </summary>
    public void ClearAllLocalCache()
    {
        _localCache.Clear();
    }

    private string GetRedisKey(string appId) => $"{_keyPrefix}{appId}";

    private async Task PublishStateChangeAsync(
        string appId,
        CircuitState newState,
        CircuitState? previousState,
        int failures,
        DateTimeOffset? openedAt,
        CancellationToken cancellationToken)
    {
        var evt = new MeshCircuitStateChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AppId = appId,
            NewState = newState,
            PreviousState = previousState,
            ConsecutiveFailures = failures,
            ChangedAt = DateTimeOffset.UtcNow,
            OpenedAt = openedAt
        };

        var published = await _messageBus.TryPublishAsync("mesh.circuit.changed", evt, cancellationToken);

        if (published)
        {
            _logger.LogInformation(
                "Circuit breaker state changed for {AppId}: {PreviousState} → {NewState} (failures: {Failures})",
                appId, previousState?.ToString() ?? "None", newState, failures);
        }
        else
        {
            _logger.LogWarning("Failed to publish circuit state change event for {AppId}", appId);
        }
    }

    private static GetStateResult ParseGetStateResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var stateStr = root.GetProperty("state").GetString() ?? "Closed";
        var state = Enum.TryParse<CircuitState>(stateStr, out var s) ? s : CircuitState.Closed;
        var failures = root.GetProperty("failures").GetInt32();
        var stateChanged = root.GetProperty("stateChanged").GetBoolean();

        DateTimeOffset? openedAt = null;
        if (root.TryGetProperty("openedAt", out var openedAtProp) && openedAtProp.ValueKind == JsonValueKind.Number)
        {
            openedAt = DateTimeOffset.FromUnixTimeMilliseconds(openedAtProp.GetInt64());
        }

        return new GetStateResult(state, failures, stateChanged, openedAt);
    }

    private static SuccessResult ParseSuccessResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var stateChanged = root.GetProperty("stateChanged").GetBoolean();

        CircuitState? previousState = null;
        if (root.TryGetProperty("previousState", out var prevProp))
        {
            var prevStr = prevProp.GetString();
            if (!string.IsNullOrEmpty(prevStr) && Enum.TryParse<CircuitState>(prevStr, out var ps))
            {
                previousState = ps;
            }
        }

        return new SuccessResult(stateChanged, previousState);
    }

    private static FailureResult ParseFailureResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var stateStr = root.GetProperty("state").GetString() ?? "Closed";
        var state = Enum.TryParse<CircuitState>(stateStr, out var s) ? s : CircuitState.Closed;
        var failures = root.GetProperty("failures").GetInt32();
        var stateChanged = root.GetProperty("stateChanged").GetBoolean();

        DateTimeOffset? openedAt = null;
        if (root.TryGetProperty("openedAt", out var openedAtProp) && openedAtProp.ValueKind == JsonValueKind.Number)
        {
            openedAt = DateTimeOffset.FromUnixTimeMilliseconds(openedAtProp.GetInt64());
        }

        return new FailureResult(state, failures, stateChanged, openedAt);
    }

    private sealed record CircuitCacheEntry(
        CircuitState State,
        DateTimeOffset? OpenedAt,
        DateTimeOffset LastUpdated)
    {
        public int Failures { get; init; }
    }

    private sealed record GetStateResult(
        CircuitState State,
        int Failures,
        bool StateChanged,
        DateTimeOffset? OpenedAt);

    private sealed record SuccessResult(
        bool StateChanged,
        CircuitState? PreviousState);

    private sealed record FailureResult(
        CircuitState State,
        int Failures,
        bool StateChanged,
        DateTimeOffset? OpenedAt);
}
