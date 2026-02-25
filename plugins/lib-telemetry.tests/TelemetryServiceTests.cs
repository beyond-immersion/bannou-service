using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Telemetry;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for TelemetryService.
/// Tests verify health check conditional logic and status fallback behavior.
/// </summary>
public class TelemetryServiceTests
{
    private readonly Mock<ILogger<TelemetryService>> _mockLogger;
    private readonly TelemetryServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public TelemetryServiceTests()
    {
        _mockLogger = new Mock<ILogger<TelemetryService>>();
        _configuration = new TelemetryServiceConfiguration();
        _appConfiguration = new AppConfiguration();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
    }

    private TelemetryService CreateService()
    {
        return new TelemetryService(
            _mockLogger.Object,
            _configuration,
            _appConfiguration,
            _mockTelemetryProvider.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void TelemetryService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<TelemetryService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void TelemetryServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new TelemetryServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region HealthAsync Tests

    /// <summary>
    /// Verifies that when both tracing and metrics are enabled,
    /// OtlpEndpoint is included in the response.
    /// </summary>
    [Fact]
    public async Task HealthAsync_BothEnabled_IncludesOtlpEndpoint()
    {
        // Arrange
        _configuration.TracingEnabled = true;
        _configuration.MetricsEnabled = true;
        _configuration.OtlpEndpoint = "http://jaeger:4317";
        var service = CreateService();

        // Act
        var (status, response) = await service.HealthAsync(new TelemetryHealthRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.TracingEnabled);
        Assert.True(response.MetricsEnabled);
        Assert.Equal("http://jaeger:4317", response.OtlpEndpoint);
    }

    /// <summary>
    /// Verifies that when both tracing and metrics are disabled,
    /// OtlpEndpoint is null in the response.
    /// </summary>
    [Fact]
    public async Task HealthAsync_BothDisabled_OtlpEndpointIsNull()
    {
        // Arrange
        _configuration.TracingEnabled = false;
        _configuration.MetricsEnabled = false;
        _configuration.OtlpEndpoint = "http://jaeger:4317";
        var service = CreateService();

        // Act
        var (status, response) = await service.HealthAsync(new TelemetryHealthRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.TracingEnabled);
        Assert.False(response.MetricsEnabled);
        Assert.Null(response.OtlpEndpoint);
    }

    /// <summary>
    /// Verifies that when only tracing is enabled, OtlpEndpoint is still included.
    /// </summary>
    [Fact]
    public async Task HealthAsync_TracingOnlyEnabled_IncludesOtlpEndpoint()
    {
        // Arrange
        _configuration.TracingEnabled = true;
        _configuration.MetricsEnabled = false;
        _configuration.OtlpEndpoint = "http://jaeger:4317";
        var service = CreateService();

        // Act
        var (_, response) = await service.HealthAsync(new TelemetryHealthRequest());

        // Assert
        Assert.NotNull(response);
        Assert.Equal("http://jaeger:4317", response.OtlpEndpoint);
    }

    /// <summary>
    /// Verifies that when only metrics is enabled, OtlpEndpoint is still included.
    /// </summary>
    [Fact]
    public async Task HealthAsync_MetricsOnlyEnabled_IncludesOtlpEndpoint()
    {
        // Arrange
        _configuration.TracingEnabled = false;
        _configuration.MetricsEnabled = true;
        _configuration.OtlpEndpoint = "http://prometheus:4317";
        var service = CreateService();

        // Act
        var (_, response) = await service.HealthAsync(new TelemetryHealthRequest());

        // Assert
        Assert.NotNull(response);
        Assert.Equal("http://prometheus:4317", response.OtlpEndpoint);
    }

    #endregion

    #region StatusAsync Tests

    /// <summary>
    /// Verifies that when ServiceName is configured, it is used in the response.
    /// </summary>
    [Fact]
    public async Task StatusAsync_WithServiceName_UsesConfiguredName()
    {
        // Arrange
        _configuration.ServiceName = "my-telemetry-service";
        var service = CreateService();

        // Act
        var (status, response) = await service.StatusAsync(new TelemetryStatusRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("my-telemetry-service", response.ServiceName);
    }

    /// <summary>
    /// Verifies that when ServiceName is null, EffectiveAppId is used as fallback.
    /// </summary>
    [Fact]
    public async Task StatusAsync_NullServiceName_FallsBackToEffectiveAppId()
    {
        // Arrange
        _configuration.ServiceName = null;
        _appConfiguration.AppId = "my-app-id";
        var service = CreateService();

        // Act
        var (_, response) = await service.StatusAsync(new TelemetryStatusRequest());

        // Assert
        Assert.NotNull(response);
        Assert.Equal("my-app-id", response.ServiceName);
    }

    /// <summary>
    /// Verifies that when ServiceName is whitespace, EffectiveAppId is used as fallback.
    /// </summary>
    [Fact]
    public async Task StatusAsync_WhitespaceServiceName_FallsBackToEffectiveAppId()
    {
        // Arrange
        _configuration.ServiceName = "   ";
        _appConfiguration.AppId = "fallback-app";
        var service = CreateService();

        // Act
        var (_, response) = await service.StatusAsync(new TelemetryStatusRequest());

        // Assert
        Assert.NotNull(response);
        Assert.Equal("fallback-app", response.ServiceName);
    }

    /// <summary>
    /// Verifies that all configuration fields are populated in the status response.
    /// </summary>
    [Fact]
    public async Task StatusAsync_PopulatesAllConfigFields()
    {
        // Arrange
        _configuration.TracingEnabled = true;
        _configuration.MetricsEnabled = false;
        _configuration.TracingSamplingRatio = 0.5;
        _configuration.ServiceName = "test-service";
        _configuration.ServiceNamespace = "test-ns";
        _configuration.DeploymentEnvironment = "staging";
        _configuration.OtlpEndpoint = "http://collector:4317";
        _configuration.OtlpProtocol = OtlpProtocol.Http;
        var service = CreateService();

        // Act
        var (status, response) = await service.StatusAsync(new TelemetryStatusRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.TracingEnabled);
        Assert.False(response.MetricsEnabled);
        Assert.Equal(0.5, response.SamplingRatio);
        Assert.Equal("test-service", response.ServiceName);
        Assert.Equal("test-ns", response.ServiceNamespace);
        Assert.Equal("staging", response.DeploymentEnvironment);
        Assert.Equal("http://collector:4317", response.OtlpEndpoint);
        Assert.Equal(OtlpProtocol.Http, response.OtlpProtocol);
    }

    #endregion
}
