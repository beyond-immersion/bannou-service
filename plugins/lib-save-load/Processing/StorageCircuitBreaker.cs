using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.SaveLoad;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Processing;

/// <summary>
/// Circuit breaker for storage operations to prevent cascading failures.
/// Uses distributed state in Redis for multi-instance coordination.
/// </summary>
public class StorageCircuitBreaker
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly ILogger<StorageCircuitBreaker> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Circuit breaker states.
    /// </summary>
    public enum CircuitState
    {
        /// <summary>Normal operation, tracking consecutive failures.</summary>
        Closed,
        /// <summary>Rejecting requests, waiting for reset timeout.</summary>
        Open,
        /// <summary>Allowing limited attempts to test recovery.</summary>
        HalfOpen
    }

    /// <summary>
    /// Internal state model stored in Redis.
    /// </summary>
    private class CircuitBreakerState
    {
        /// <summary>Current circuit state.</summary>
        public string State { get; set; } = "Closed";
        /// <summary>Count of consecutive failures.</summary>
        public int ConsecutiveFailures { get; set; }
        /// <summary>When the circuit was opened.</summary>
        public DateTimeOffset? OpenedAt { get; set; }
        /// <summary>Count of half-open attempts.</summary>
        public int HalfOpenAttempts { get; set; }
        /// <summary>Last state change timestamp.</summary>
        public DateTimeOffset LastStateChange { get; set; } = DateTimeOffset.UtcNow;
    }

    private const string CircuitStateKey = "circuit:storage";

    /// <summary>
    /// Creates a new StorageCircuitBreaker instance.
    /// </summary>
    public StorageCircuitBreaker(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        SaveLoadServiceConfiguration configuration,
        ILogger<StorageCircuitBreaker> logger,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Gets the current circuit state.
    /// </summary>
    public async Task<CircuitState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.GetStateAsync");
        var state = await GetOrCreateStateAsync(cancellationToken);
        return ParseState(state.State);
    }

    /// <summary>
    /// Checks if an operation is allowed based on circuit state.
    /// Returns true if allowed, false if circuit is open.
    /// Always allows if circuit breaker is disabled.
    /// </summary>
    public async Task<bool> IsAllowedAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.IsAllowedAsync");
        if (!_configuration.StorageCircuitBreakerEnabled)
        {
            return true;
        }

        var state = await GetOrCreateStateAsync(cancellationToken);
        var currentState = ParseState(state.State);

        switch (currentState)
        {
            case CircuitState.Closed:
                return true;

            case CircuitState.Open:
                // Check if reset timeout has passed
                if (state.OpenedAt.HasValue &&
                    DateTimeOffset.UtcNow - state.OpenedAt.Value >= TimeSpan.FromSeconds(_configuration.StorageCircuitBreakerResetSeconds))
                {
                    await TransitionToHalfOpenAsync(state, cancellationToken);
                    return true;
                }
                return false;

            case CircuitState.HalfOpen:
                // Allow limited attempts in half-open state
                return state.HalfOpenAttempts < _configuration.StorageCircuitBreakerHalfOpenAttempts;

            default:
                return true;
        }
    }

    /// <summary>
    /// Records a successful operation.
    /// </summary>
    public async Task RecordSuccessAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.RecordSuccessAsync");
        var state = await GetOrCreateStateAsync(cancellationToken);
        var currentState = ParseState(state.State);

        switch (currentState)
        {
            case CircuitState.HalfOpen:
                // Success in half-open state closes the circuit
                await TransitionToClosedAsync(state, cancellationToken);
                break;

            case CircuitState.Closed:
                // Reset consecutive failures on success
                if (state.ConsecutiveFailures > 0)
                {
                    state.ConsecutiveFailures = 0;
                    await SaveStateAsync(state, cancellationToken);
                }
                break;
        }
    }

    /// <summary>
    /// Records a failed operation.
    /// </summary>
    public async Task RecordFailureAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.RecordFailureAsync");
        var state = await GetOrCreateStateAsync(cancellationToken);
        var currentState = ParseState(state.State);

        switch (currentState)
        {
            case CircuitState.HalfOpen:
                // Failure in half-open state reopens the circuit
                state.HalfOpenAttempts++;
                if (state.HalfOpenAttempts >= _configuration.StorageCircuitBreakerHalfOpenAttempts)
                {
                    await TransitionToOpenAsync(state, cancellationToken);
                }
                else
                {
                    await SaveStateAsync(state, cancellationToken);
                }
                break;

            case CircuitState.Closed:
                state.ConsecutiveFailures++;
                if (state.ConsecutiveFailures >= _configuration.StorageCircuitBreakerThreshold)
                {
                    await TransitionToOpenAsync(state, cancellationToken);
                }
                else
                {
                    await SaveStateAsync(state, cancellationToken);
                }
                break;
        }
    }

    /// <summary>
    /// Resets the circuit breaker to closed state (admin operation).
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.ResetAsync");
        var state = await GetOrCreateStateAsync(cancellationToken);
        if (ParseState(state.State) != CircuitState.Closed)
        {
            await TransitionToClosedAsync(state, cancellationToken);
            _logger.LogInformation("Circuit breaker manually reset to closed state");
        }
    }

    private async Task TransitionToOpenAsync(CircuitBreakerState state, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.TransitionToOpenAsync");
        var previousState = state.State;
        state.State = "Open";
        state.OpenedAt = DateTimeOffset.UtcNow;
        state.HalfOpenAttempts = 0;
        state.LastStateChange = DateTimeOffset.UtcNow;

        await SaveStateAsync(state, cancellationToken);

        _logger.LogWarning(
            "Circuit breaker OPENED after {FailureCount} consecutive failures",
            state.ConsecutiveFailures);

        await PublishStateChangeAsync(previousState, "Open", state.ConsecutiveFailures, cancellationToken);
    }

    private async Task TransitionToHalfOpenAsync(CircuitBreakerState state, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.TransitionToHalfOpenAsync");
        var previousState = state.State;
        state.State = "HalfOpen";
        state.HalfOpenAttempts = 0;
        state.LastStateChange = DateTimeOffset.UtcNow;

        await SaveStateAsync(state, cancellationToken);

        _logger.LogInformation("Circuit breaker transitioning to HALF-OPEN state");

        await PublishStateChangeAsync(previousState, "HalfOpen", 0, cancellationToken);
    }

    private async Task TransitionToClosedAsync(CircuitBreakerState state, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.TransitionToClosedAsync");
        var previousState = state.State;
        state.State = "Closed";
        state.ConsecutiveFailures = 0;
        state.OpenedAt = null;
        state.HalfOpenAttempts = 0;
        state.LastStateChange = DateTimeOffset.UtcNow;

        await SaveStateAsync(state, cancellationToken);

        _logger.LogInformation("Circuit breaker CLOSED - storage operations resuming normally");

        await PublishStateChangeAsync(previousState, "Closed", 0, cancellationToken);
    }

    private async Task PublishStateChangeAsync(string previousState, string newState, int failureCount, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.PublishStateChangeAsync");
        try
        {
            var stateChangeEvent = new CircuitBreakerStateChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                PreviousState = ParsePreviousState(previousState),
                NewState = ParseNewState(newState),
                FailureCount = failureCount
            };

            await _messageBus.TryPublishAsync(
                "save.circuit-breaker-changed",
                stateChangeEvent,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish circuit breaker state change event");
        }
    }

    private static CircuitBreakerStateChangedEventPreviousState ParsePreviousState(string state)
    {
        return state switch
        {
            "Open" => CircuitBreakerStateChangedEventPreviousState.OPEN,
            "HalfOpen" => CircuitBreakerStateChangedEventPreviousState.HALF_OPEN,
            _ => CircuitBreakerStateChangedEventPreviousState.CLOSED
        };
    }

    private static CircuitBreakerStateChangedEventNewState ParseNewState(string state)
    {
        return state switch
        {
            "Open" => CircuitBreakerStateChangedEventNewState.OPEN,
            "HalfOpen" => CircuitBreakerStateChangedEventNewState.HALF_OPEN,
            _ => CircuitBreakerStateChangedEventNewState.CLOSED
        };
    }

    private async Task<CircuitBreakerState> GetOrCreateStateAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.GetOrCreateStateAsync");
        var store = _stateStoreFactory.GetStore<CircuitBreakerState>(StateStoreDefinitions.SaveLoadPending);
        var state = await store.GetAsync(CircuitStateKey, cancellationToken);
        return state ?? new CircuitBreakerState();
    }

    private async Task SaveStateAsync(CircuitBreakerState state, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "StorageCircuitBreaker.SaveStateAsync");
        var store = _stateStoreFactory.GetStore<CircuitBreakerState>(StateStoreDefinitions.SaveLoadPending);
        await store.SaveAsync(CircuitStateKey, state, cancellationToken: cancellationToken);
    }

    private static CircuitState ParseState(string state)
    {
        return state switch
        {
            "Open" => CircuitState.Open,
            "HalfOpen" => CircuitState.HalfOpen,
            _ => CircuitState.Closed
        };
    }
}
