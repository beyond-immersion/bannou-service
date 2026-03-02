using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Website;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Website.Tests;

/// <summary>
/// Unit tests for WebsiteService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class WebsiteServiceTests
{
    private readonly Mock<ILogger<WebsiteService>> _mockLogger;
    private readonly Mock<WebsiteServiceConfiguration> _mockConfiguration;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public WebsiteServiceTests()
    {
        _mockLogger = new Mock<ILogger<WebsiteService>>();
        _mockConfiguration = new Mock<WebsiteServiceConfiguration>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = new WebsiteService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockMessageBus.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);

        Assert.NotNull(service);
    }


    // Note: WebsiteService methods are not yet implemented - planned for future release.
    // Additional tests will be added when service implementation begins.
}
