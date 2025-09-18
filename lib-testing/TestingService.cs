using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Testing service implementation that exercises the plugin system properly.
/// Implements IDaprService to test centralized service resolution.
/// </summary>
[DaprService("testing", interfaceType: typeof(ITestingService), priority: false, lifetime: ServiceLifetime.Scoped)]
public class TestingService : ITestingService, IDaprService
{
    private readonly ILogger<TestingService> _logger;
    private readonly TestingServiceConfiguration _configuration;

    public TestingService(
        ILogger<TestingService> logger,
        TestingServiceConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Simple test method to verify service is working.
    /// </summary>
    public Task<(StatusCodes, TestResponse?)> RunTestAsync(string testName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🧪 Running test: {TestName}", testName);

            var response = new TestResponse
            {
                TestName = testName,
                Success = true,
                Message = $"Test {testName} completed successfully",
                Timestamp = DateTime.UtcNow
            };

            return Task.FromResult<(StatusCodes, TestResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error running test: {TestName}", testName);
            return Task.FromResult<(StatusCodes, TestResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Test method to verify configuration is working.
    /// </summary>
    public Task<(StatusCodes, ConfigTestResponse?)> TestConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔧 Testing configuration");

            var response = new ConfigTestResponse
            {
                ConfigLoaded = _configuration != null,
                Message = "Configuration test completed",
                Timestamp = DateTime.UtcNow
            };

            return Task.FromResult<(StatusCodes, ConfigTestResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error testing configuration");
            return Task.FromResult<(StatusCodes, ConfigTestResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    // IDaprService lifecycle methods are provided by default interface implementations
    // No need to override unless custom logic is required beyond the default logging
}

/// <summary>
/// Testing service interface for dependency injection.
/// </summary>
public interface ITestingService
{
    Task<(StatusCodes, TestResponse?)> RunTestAsync(string testName, CancellationToken cancellationToken = default);
    Task<(StatusCodes, ConfigTestResponse?)> TestConfigurationAsync(CancellationToken cancellationToken = default);
}


/// <summary>
/// Response model for test operations.
/// </summary>
public class TestResponse
{
    public string TestName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response model for configuration tests.
/// </summary>
public class ConfigTestResponse
{
    public bool ConfigLoaded { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Status codes for testing service responses.
/// </summary>
public enum StatusCodes
{
    OK = 200,
    BadRequest = 400,
    InternalServerError = 500
}
