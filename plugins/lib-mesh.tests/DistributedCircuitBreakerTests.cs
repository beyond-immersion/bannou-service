#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace BeyondImmersion.BannouService.Mesh.Tests;

/// <summary>
/// Tests for DistributedCircuitBreaker - the distributed circuit breaker implementation
/// that shares state across mesh instances via Redis and RabbitMQ events.
/// </summary>
public class DistributedCircuitBreakerTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IRedisOperations> _mockRedisOperations;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger> _mockLogger;

    private const int DefaultThreshold = 3;
    private static readonly TimeSpan DefaultResetTimeout = TimeSpan.FromSeconds(30);

    public DistributedCircuitBreakerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRedisOperations = new Mock<IRedisOperations>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger>();

        // Default: return the mock Redis operations
        _mockStateStoreFactory.Setup(x => x.GetRedisOperations())
            .Returns(_mockRedisOperations.Object);

        // Default: message bus publishes successfully
        _mockMessageBus.Setup(x => x.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<MeshCircuitStateChangedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private DistributedCircuitBreaker CreateCircuitBreaker(
        int threshold = DefaultThreshold,
        TimeSpan? resetTimeout = null)
    {
        return new DistributedCircuitBreaker(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            new NullTelemetryProvider(),
            threshold,
            resetTimeout ?? DefaultResetTimeout);
    }

    #region State Transition Tests

    [Fact]
    public async Task GetStateAsync_WhenCircuitNotInCache_ReturnsClosedFromRedis()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker();
        SetupRedisGetCircuitState("test-app", CircuitState.Closed, 0);

        // Act
        var state = await circuitBreaker.GetStateAsync("test-app");

        // Assert
        Assert.Equal(CircuitState.Closed, state);
        _mockRedisOperations.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailureAsync_ReachesThreshold_CircuitOpens()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker(threshold: 3);
        SetupRedisRecordFailure("test-app", CircuitState.Open, 3, stateChanged: true);

        // Act - Record failures to reach threshold
        await circuitBreaker.RecordFailureAsync("test-app");

        // Assert - Event should be published for state change
        _mockMessageBus.Verify(x => x.TryPublishAsync(
            "mesh.circuit.changed",
            It.Is<MeshCircuitStateChangedEvent>(e =>
                e.AppId == "test-app" &&
                e.NewState == CircuitState.Open),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordSuccessAsync_WhenOpen_CircuitCloses()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker();

        // Simulate circuit in Open state (from previous failures)
        circuitBreaker.HandleStateChangeEvent(new MeshCircuitStateChangedEvent
        {
            AppId = "test-app",
            NewState = CircuitState.Open,
            ConsecutiveFailures = 3,
            ChangedAt = DateTimeOffset.UtcNow,
            OpenedAt = DateTimeOffset.UtcNow
        });

        SetupRedisRecordSuccess("test-app", stateChanged: true, previousState: CircuitState.Open);

        // Act
        await circuitBreaker.RecordSuccessAsync("test-app");

        // Assert - Event should be published for state change
        _mockMessageBus.Verify(x => x.TryPublishAsync(
            "mesh.circuit.changed",
            It.Is<MeshCircuitStateChangedEvent>(e =>
                e.AppId == "test-app" &&
                e.NewState == CircuitState.Closed &&
                e.PreviousState == CircuitState.Open),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStateAsync_WhenOpenAndTimeoutElapsed_TransitionsToHalfOpen()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker(resetTimeout: TimeSpan.FromSeconds(1));

        // Put circuit in Open state with old openedAt
        var oldOpenedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        circuitBreaker.HandleStateChangeEvent(new MeshCircuitStateChangedEvent
        {
            AppId = "test-app",
            NewState = CircuitState.Open,
            ConsecutiveFailures = 3,
            ChangedAt = oldOpenedAt,
            OpenedAt = oldOpenedAt
        });

        // Act - Get state after timeout elapsed
        var state = await circuitBreaker.GetStateAsync("test-app");

        // Assert - Should transition to HalfOpen from local cache
        Assert.Equal(CircuitState.HalfOpen, state);
    }

    [Fact]
    public async Task RecordFailureAsync_WhenHalfOpen_CircuitReopens()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker(threshold: 3);

        // Put circuit in HalfOpen state
        circuitBreaker.HandleStateChangeEvent(new MeshCircuitStateChangedEvent
        {
            AppId = "test-app",
            NewState = CircuitState.HalfOpen,
            ConsecutiveFailures = 3,
            ChangedAt = DateTimeOffset.UtcNow
        });

        // Setup Redis to return Open after failure in HalfOpen
        SetupRedisRecordFailure("test-app", CircuitState.Open, 3, stateChanged: true);

        // Act - Probe request fails
        await circuitBreaker.RecordFailureAsync("test-app");

        // Assert - Circuit should reopen
        _mockMessageBus.Verify(x => x.TryPublishAsync(
            "mesh.circuit.changed",
            It.Is<MeshCircuitStateChangedEvent>(e =>
                e.AppId == "test-app" &&
                e.NewState == CircuitState.Open),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Event Handling Tests

    [Fact]
    public async Task HandleStateChangeEvent_UpdatesLocalCache()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker();

        var evt = new MeshCircuitStateChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AppId = "other-instance-app",
            NewState = CircuitState.Open,
            ConsecutiveFailures = 5,
            ChangedAt = DateTimeOffset.UtcNow,
            OpenedAt = DateTimeOffset.UtcNow
        };

        // Act
        circuitBreaker.HandleStateChangeEvent(evt);

        // Assert - Next GetState should use cache (no Redis call)
        var state = await circuitBreaker.GetStateAsync("other-instance-app");
        Assert.Equal(CircuitState.Open, state);

        // Verify no Redis call was made (cache hit)
        _mockRedisOperations.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Local Cache Tests

    [Fact]
    public async Task GetStateAsync_UsesCacheOnSubsequentCalls()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker();
        SetupRedisGetCircuitState("cached-app", CircuitState.Closed, 0);

        // Act - First call populates cache
        await circuitBreaker.GetStateAsync("cached-app");

        // Second call should use cache
        await circuitBreaker.GetStateAsync("cached-app");

        // Assert - Redis should only be called once
        _mockRedisOperations.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearLocalCache_RemovesEntry()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker();

        // Populate cache via event
        circuitBreaker.HandleStateChangeEvent(new MeshCircuitStateChangedEvent
        {
            AppId = "clear-test",
            NewState = CircuitState.Open,
            ConsecutiveFailures = 3,
            ChangedAt = DateTimeOffset.UtcNow
        });

        // Act
        circuitBreaker.ClearLocalCache("clear-test");

        // Setup Redis for next call
        SetupRedisGetCircuitState("clear-test", CircuitState.Closed, 0);

        // Assert - Next call should hit Redis
        var state = await circuitBreaker.GetStateAsync("clear-test");
        Assert.Equal(CircuitState.Closed, state);

        _mockRedisOperations.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Graceful Degradation Tests

    [Fact]
    public async Task GetStateAsync_WhenRedisUnavailable_ReturnsClosedGracefully()
    {
        // Arrange - Return null for Redis operations (InMemory mode)
        _mockStateStoreFactory.Setup(x => x.GetRedisOperations())
            .Returns((IRedisOperations?)null);

        var circuitBreaker = CreateCircuitBreaker();

        // Act
        var state = await circuitBreaker.GetStateAsync("no-redis-app");

        // Assert - Should gracefully return Closed
        Assert.Equal(CircuitState.Closed, state);
    }

    [Fact]
    public async Task RecordFailureAsync_WhenRedisUnavailable_UsesLocalOnlyTracking()
    {
        // Arrange - Return null for Redis operations (InMemory mode)
        _mockStateStoreFactory.Setup(x => x.GetRedisOperations())
            .Returns((IRedisOperations?)null);

        var circuitBreaker = CreateCircuitBreaker(threshold: 2);

        // Act - Record failures locally
        await circuitBreaker.RecordFailureAsync("local-app");
        await circuitBreaker.RecordFailureAsync("local-app");

        // Assert - Should open locally after threshold
        var state = await circuitBreaker.GetStateAsync("local-app");
        Assert.Equal(CircuitState.Open, state);
    }

    [Fact]
    public async Task GetStateAsync_WhenRedisThrows_FallsBackToLocalCacheOrClosed()
    {
        // Arrange
        _mockRedisOperations.Setup(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test failure"));

        var circuitBreaker = CreateCircuitBreaker();

        // Act
        var state = await circuitBreaker.GetStateAsync("failing-redis-app");

        // Assert - Should gracefully return Closed
        Assert.Equal(CircuitState.Closed, state);
    }

    #endregion

    #region Event Publishing Tests

    [Fact]
    public async Task RecordFailureAsync_WhenStateChanges_PublishesEvent()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker(threshold: 1);
        SetupRedisRecordFailure("publish-test", CircuitState.Open, 1, stateChanged: true);

        // Act
        await circuitBreaker.RecordFailureAsync("publish-test");

        // Assert
        _mockMessageBus.Verify(x => x.TryPublishAsync(
            "mesh.circuit.changed",
            It.Is<MeshCircuitStateChangedEvent>(e =>
                e.AppId == "publish-test" &&
                e.NewState == CircuitState.Open &&
                e.ConsecutiveFailures == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailureAsync_WhenStateUnchanged_DoesNotPublishEvent()
    {
        // Arrange
        var circuitBreaker = CreateCircuitBreaker(threshold: 3);
        SetupRedisRecordFailure("no-publish-test", CircuitState.Closed, 1, stateChanged: false);

        // Act
        await circuitBreaker.RecordFailureAsync("no-publish-test");

        // Assert - No event should be published
        _mockMessageBus.Verify(x => x.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<MeshCircuitStateChangedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private void SetupRedisGetCircuitState(string appId, CircuitState state, int failures, bool stateChanged = false)
    {
        var jsonResponse = $"{{\"state\":\"{state}\",\"failures\":{failures},\"stateChanged\":{stateChanged.ToString().ToLower()},\"openedAt\":null}}";

        _mockRedisOperations.Setup(x => x.ScriptEvaluateAsync(
            MeshLuaScripts.GetCircuitState,
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString().Contains(appId)),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue(jsonResponse)));
    }

    private void SetupRedisRecordFailure(string appId, CircuitState state, int failures, bool stateChanged)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var openedAtJson = state == CircuitState.Open ? nowMs.ToString() : "null";
        var jsonResponse = $"{{\"failures\":{failures},\"state\":\"{state}\",\"stateChanged\":{stateChanged.ToString().ToLower()},\"openedAt\":{openedAtJson}}}";

        _mockRedisOperations.Setup(x => x.ScriptEvaluateAsync(
            MeshLuaScripts.RecordCircuitFailure,
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString().Contains(appId)),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue(jsonResponse)));
    }

    private void SetupRedisRecordSuccess(string appId, bool stateChanged, CircuitState? previousState = null)
    {
        var prevStateJson = previousState.HasValue ? $"\"{previousState.Value}\"" : "\"Closed\"";
        var jsonResponse = $"{{\"state\":\"Closed\",\"stateChanged\":{stateChanged.ToString().ToLower()},\"previousState\":{prevStateJson}}}";

        _mockRedisOperations.Setup(x => x.ScriptEvaluateAsync(
            MeshLuaScripts.RecordCircuitSuccess,
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString().Contains(appId)),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue(jsonResponse)));
    }

    #endregion
}
