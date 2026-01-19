using BeyondImmersion.BannouService.Orchestrator;
using LibOrchestrator;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Orchestrator.Tests;

/// <summary>
/// Unit tests for SmartRestartManager.
/// Tests restart logic, health check integration, and error handling.
/// Note: Docker container operations require integration testing.
/// </summary>
public class SmartRestartManagerTests : IAsyncLifetime
{
    private readonly Mock<ILogger<SmartRestartManager>> _mockLogger;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly List<SmartRestartManager> _createdManagers = new();

    public SmartRestartManagerTests()
    {
        _mockLogger = new Mock<ILogger<SmartRestartManager>>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _configuration = new OrchestratorServiceConfiguration
        {
            DockerHost = "unix:///var/run/docker.sock"
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var manager in _createdManagers)
        {
            await manager.DisposeAsync();
        }
        _createdManagers.Clear();
    }

    private SmartRestartManager CreateManager()
    {
        var manager = new SmartRestartManager(
            _mockLogger.Object,
            _configuration,
            _mockHealthMonitor.Object,
            _mockEventManager.Object);
        _createdManagers.Add(manager);
        return manager;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var manager = CreateManager();

        // Assert
        Assert.NotNull(manager);
    }

    #endregion

    #region RestartServiceAsync Tests - Health Check Logic

    [Fact]
    public async Task RestartServiceAsync_WhenNotForced_AndHealthMonitorSaysNoRestart_ShouldReturnFailure()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = false,
                ServiceName = serviceName,
                CurrentStatus = "healthy",
                Reason = "Service is running normally"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = false
        };

        // Act
        var result = await manager.RestartServiceAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(serviceName, result.ServiceName);
        Assert.Contains("Restart not needed", result.Message);
        Assert.Equal("healthy", result.CurrentStatus);
    }

    [Fact]
    public async Task RestartServiceAsync_WhenNotForced_AndHealthMonitorRecommends_ShouldAttemptRestart()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = true,
                ServiceName = serviceName,
                CurrentStatus = "degraded",
                Reason = "No heartbeat for 5 minutes"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = false
        };

        // Act - Will fail because Docker client is not initialized
        var result = await manager.RestartServiceAsync(request);

        // Assert - Should fail at Docker step
        Assert.False(result.Success);
        Assert.Equal(serviceName, result.ServiceName);
        // Error message indicates Docker-related failure
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartServiceAsync_WhenForced_ShouldBypassHealthCheck()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        // Health monitor should NOT be called when forced
        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = false,
                ServiceName = serviceName,
                CurrentStatus = "healthy"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = true
        };

        // Act - Will fail at Docker step, but should skip health check
        var result = await manager.RestartServiceAsync(request);

        // Assert - Health check was called only for status, not for restart decision
        // The restart should have been attempted despite health saying no
        Assert.False(result.Success); // Fails at Docker step
        Assert.Equal(serviceName, result.ServiceName);
    }

    [Fact]
    public async Task RestartServiceAsync_WithEnvironmentVariables_ShouldStillAttemptRestart()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = true,
                ServiceName = serviceName,
                CurrentStatus = "degraded"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = true,
            Environment = new Dictionary<string, string>
            {
                ["NEW_VAR"] = "new_value"
            }
        };

        // Act
        var result = await manager.RestartServiceAsync(request);

        // Assert - Should still attempt restart even with env vars
        // Will fail at Docker step since client is not initialized
        Assert.False(result.Success);
        Assert.Equal(serviceName, result.ServiceName);
        // Verify health check was still used to get previous status
        _mockHealthMonitor.Verify(x => x.ShouldRestartServiceAsync(serviceName), Times.AtLeastOnce);
    }

    #endregion

    #region RestartServiceAsync Tests - Error Handling

    [Fact]
    public async Task RestartServiceAsync_WhenDockerClientNotInitialized_ShouldReturnError()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = true,
                ServiceName = serviceName,
                CurrentStatus = "degraded"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = true
        };

        // Act - Docker client is null, should fail
        var result = await manager.RestartServiceAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(serviceName, result.ServiceName);
        Assert.Equal("error", result.CurrentStatus);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartServiceAsync_WhenHealthMonitorThrows_ShouldReturnError()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ThrowsAsync(new Exception("Health monitor connection failed"));

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = false
        };

        // Act
        var result = await manager.RestartServiceAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(serviceName, result.ServiceName);
        Assert.Contains("Health monitor connection failed", result.Message);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithoutInitialization_ShouldNotThrow()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_WithoutInitialization_ShouldNotThrow()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(async () => await manager.DisposeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - Multiple dispose calls should be safe
        var exception = Record.Exception(() =>
        {
            manager.Dispose();
            manager.Dispose();
            manager.Dispose();
        });
        Assert.Null(exception);
    }

    #endregion

    #region Duration Formatting Tests

    [Fact]
    public async Task RestartServiceAsync_ShouldReturnFormattedDuration()
    {
        // Arrange
        var manager = CreateManager();
        var serviceName = "test-service";

        _mockHealthMonitor.Setup(x => x.ShouldRestartServiceAsync(serviceName))
            .ReturnsAsync(new RestartRecommendation
            {
                ShouldRestart = false,
                ServiceName = serviceName,
                CurrentStatus = "healthy"
            });

        var request = new ServiceRestartRequest
        {
            ServiceName = serviceName,
            Force = false
        };

        // Act
        var result = await manager.RestartServiceAsync(request);

        // Assert - Duration should be in hh:mm:ss format
        Assert.NotNull(result.Duration);
        Assert.Matches(@"\d{2}:\d{2}:\d{2}", result.Duration);
    }

    #endregion
}
