using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Mesh.Tests;

/// <summary>
/// Tests for LocalMeshStateManager - the local-only state manager for
/// testing and minimal infrastructure scenarios.
/// All routing goes through a single local endpoint.
/// </summary>
public class LocalMeshStateManagerTests : IAsyncDisposable
{
    private readonly Mock<ILogger<LocalMeshStateManager>> _mockLogger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly LocalMeshStateManager _manager;
    private readonly Guid _instanceId;

    public LocalMeshStateManagerTests()
    {
        _mockLogger = new Mock<ILogger<LocalMeshStateManager>>();
        _configuration = new MeshServiceConfiguration();
        _instanceId = Guid.NewGuid();

        var mockIdentifier = new Mock<IMeshInstanceIdentifier>();
        mockIdentifier.Setup(x => x.InstanceId).Returns(_instanceId);

        _manager = new LocalMeshStateManager(
            _configuration,
            _mockLogger.Object,
            mockIdentifier.Object);
    }

    public ValueTask DisposeAsync()
    {
        return _manager.DisposeAsync();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldNotThrow_WithValidDependencies()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<LocalMeshStateManager>();
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_ShouldReturnTrue()
    {
        // Act
        var result = await _manager.InitializeAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task InitializeAsync_WithCancellationToken_ShouldReturnTrue()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _manager.InitializeAsync(cts.Token);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region CheckHealthAsync Tests

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnHealthy()
    {
        // Act
        var (isHealthy, message, operationTime) = await _manager.CheckHealthAsync();

        // Assert
        Assert.True(isHealthy);
        Assert.NotNull(message);
        Assert.Contains("Local routing mode", message);
        Assert.Equal(TimeSpan.Zero, operationTime);
    }

    #endregion

    #region RegisterEndpointAsync Tests

    [Fact]
    public async Task RegisterEndpointAsync_ShouldReturnTrue_AndIgnoreRegistration()
    {
        // Arrange
        var endpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "test-app",
            Host = "remote-host",
            Port = 5000,
            Status = EndpointStatus.Healthy
        };

        // Act
        var result = await _manager.RegisterEndpointAsync(endpoint, 90);

        // Assert - always returns true (no-op in local mode)
        Assert.True(result);
    }

    #endregion

    #region DeregisterEndpointAsync Tests

    [Fact]
    public async Task DeregisterEndpointAsync_ShouldReturnTrue_AndIgnoreDeregistration()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var result = await _manager.DeregisterEndpointAsync(instanceId, "test-app");

        // Assert - always returns true (no-op in local mode)
        Assert.True(result);
    }

    #endregion

    #region UpdateHeartbeatAsync Tests

    [Fact]
    public async Task UpdateHeartbeatAsync_ShouldReturnTrue_AndIgnoreHeartbeat()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var result = await _manager.UpdateHeartbeatAsync(
            instanceId, "test-app", EndpointStatus.Healthy, 50f, 100, null, 90);

        // Assert - always returns true (no-op in local mode)
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithIssues_ShouldReturnTrue()
    {
        // Act
        var result = await _manager.UpdateHeartbeatAsync(
            Guid.NewGuid(), "test-app", EndpointStatus.Degraded,
            75f, 200, new List<string> { "disk low" }, 90);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetEndpointsForAppIdAsync Tests

    [Fact]
    public async Task GetEndpointsForAppIdAsync_ShouldReturnLocalEndpoint()
    {
        // Act
        var endpoints = await _manager.GetEndpointsForAppIdAsync("any-app-id");

        // Assert - always returns the single local endpoint
        Assert.Single(endpoints);
        Assert.Equal(_instanceId, endpoints[0].InstanceId);
        Assert.Equal(AppConstants.DEFAULT_APP_NAME, endpoints[0].AppId);
        Assert.Equal("localhost", endpoints[0].Host);
        Assert.Equal(80, endpoints[0].Port);
        Assert.Equal(EndpointStatus.Healthy, endpoints[0].Status);
    }

    [Fact]
    public async Task GetEndpointsForAppIdAsync_WithDifferentAppIds_ShouldAlwaysReturnSameLocalEndpoint()
    {
        // Act
        var endpoints1 = await _manager.GetEndpointsForAppIdAsync("app-a");
        var endpoints2 = await _manager.GetEndpointsForAppIdAsync("app-b");

        // Assert - same local endpoint regardless of app-id
        Assert.Single(endpoints1);
        Assert.Single(endpoints2);
        Assert.Equal(endpoints1[0].InstanceId, endpoints2[0].InstanceId);
    }

    [Fact]
    public async Task GetEndpointsForAppIdAsync_IncludeUnhealthy_ShouldStillReturnLocalEndpoint()
    {
        // Act
        var endpoints = await _manager.GetEndpointsForAppIdAsync("test", includeUnhealthy: true);

        // Assert
        Assert.Single(endpoints);
        Assert.Equal(EndpointStatus.Healthy, endpoints[0].Status);
    }

    #endregion

    #region GetAllEndpointsAsync Tests

    [Fact]
    public async Task GetAllEndpointsAsync_ShouldReturnLocalEndpoint()
    {
        // Act
        var endpoints = await _manager.GetAllEndpointsAsync();

        // Assert
        Assert.Single(endpoints);
        Assert.Equal(_instanceId, endpoints[0].InstanceId);
    }

    [Fact]
    public async Task GetAllEndpointsAsync_WithPrefix_ShouldStillReturnLocalEndpoint()
    {
        // Act - prefix is ignored in local mode
        var endpoints = await _manager.GetAllEndpointsAsync("some-prefix");

        // Assert
        Assert.Single(endpoints);
    }

    #endregion

    #region GetEndpointByInstanceIdAsync Tests

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WithLocalInstanceId_ShouldReturnLocalEndpoint()
    {
        // Act
        var endpoint = await _manager.GetEndpointByInstanceIdAsync(_instanceId);

        // Assert
        Assert.NotNull(endpoint);
        Assert.Equal(_instanceId, endpoint.InstanceId);
    }

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WithDifferentInstanceId_ShouldStillReturnLocalEndpoint()
    {
        // Act - unknown instance IDs still return the local endpoint (all routing goes local)
        var endpoint = await _manager.GetEndpointByInstanceIdAsync(Guid.NewGuid());

        // Assert
        Assert.NotNull(endpoint);
        Assert.Equal(_instanceId, endpoint.InstanceId);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LocalMeshStateManager>>();
        var mockIdentifier = new Mock<IMeshInstanceIdentifier>();
        mockIdentifier.Setup(x => x.InstanceId).Returns(Guid.NewGuid());

        var manager = new LocalMeshStateManager(
            new MeshServiceConfiguration(),
            mockLogger.Object,
            mockIdentifier.Object);

        // Act & Assert - should complete without throwing
        await manager.DisposeAsync();
    }

    #endregion
}
