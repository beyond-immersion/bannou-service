using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.SaveLoad;
using BeyondImmersion.BannouService.SaveLoad.Processing;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for the StorageCircuitBreaker public API.
/// Note: The circuit breaker uses internal state management via Redis.
/// These tests focus on the public interface behavior.
/// </summary>
public class CircuitBreakerTests
{
    private readonly Mock<IStateStoreFactory> _stateStoreFactoryMock;
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<ILogger<StorageCircuitBreaker>> _loggerMock;
    private readonly SaveLoadServiceConfiguration _configuration;

    public CircuitBreakerTests()
    {
        _stateStoreFactoryMock = new Mock<IStateStoreFactory>();
        _messageBusMock = new Mock<IMessageBus>();
        _loggerMock = new Mock<ILogger<StorageCircuitBreaker>>();

        _configuration = new SaveLoadServiceConfiguration();

        // Setup mock state store that returns null (no existing state)
        var mockStore = new Mock<IStateStore<object>>();
        mockStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        mockStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _stateStoreFactoryMock.Setup(f => f.GetStore<object>(It.IsAny<string>()))
            .Returns(mockStore.Object);
    }

    #region Basic API Tests

    [Fact]
    public void StorageCircuitBreaker_CanBeInstantiated()
    {
        // Arrange & Act
        var circuitBreaker = CreateCircuitBreaker();

        // Assert
        Assert.NotNull(circuitBreaker);
    }

    [Fact]
    public void StorageCircuitBreaker_RequiresStateStoreFactory()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StorageCircuitBreaker(
            null!,
            _messageBusMock.Object,
            _configuration,
            _loggerMock.Object));
    }

    [Fact]
    public void StorageCircuitBreaker_RequiresMessageBus()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StorageCircuitBreaker(
            _stateStoreFactoryMock.Object,
            null!,
            _configuration,
            _loggerMock.Object));
    }

    [Fact]
    public void StorageCircuitBreaker_RequiresConfiguration()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StorageCircuitBreaker(
            _stateStoreFactoryMock.Object,
            _messageBusMock.Object,
            null!,
            _loggerMock.Object));
    }

    [Fact]
    public void StorageCircuitBreaker_RequiresLogger()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StorageCircuitBreaker(
            _stateStoreFactoryMock.Object,
            _messageBusMock.Object,
            _configuration,
            null!));
    }

    #endregion

    #region State Enum Tests

    [Fact]
    public void CircuitState_HasExpectedValues()
    {
        // Arrange & Act
        var closedValue = StorageCircuitBreaker.CircuitState.Closed;
        var openValue = StorageCircuitBreaker.CircuitState.Open;
        var halfOpenValue = StorageCircuitBreaker.CircuitState.HalfOpen;

        // Assert - just verify enum values exist
        Assert.Equal(StorageCircuitBreaker.CircuitState.Closed, closedValue);
        Assert.Equal(StorageCircuitBreaker.CircuitState.Open, openValue);
        Assert.Equal(StorageCircuitBreaker.CircuitState.HalfOpen, halfOpenValue);
    }

    #endregion

    private StorageCircuitBreaker CreateCircuitBreaker()
    {
        return new StorageCircuitBreaker(
            _stateStoreFactoryMock.Object,
            _messageBusMock.Object,
            _configuration,
            _loggerMock.Object);
    }
}
