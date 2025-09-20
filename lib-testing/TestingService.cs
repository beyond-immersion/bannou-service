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
public class TestingService : ITestingService
{
    private readonly ILogger<TestingService> _logger;
    private readonly TestingServiceConfiguration _configuration;

    public TestingService(
        ILogger<TestingService> logger,
        TestingServiceConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

    /// <summary>
    /// Test method to validate dependency injection health and null safety patterns.
    /// This test validates the fixes for null-forgiving operators and constructor issues.
    /// </summary>
    public Task<(StatusCodes, DependencyTestResponse?)> TestDependencyInjectionHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 Testing dependency injection health");

            // Since we established null safety in constructor with proper null checks,
            // we know both _logger and _configuration are not null
            var dependencyHealthChecks = new List<(string Name, bool IsHealthy, string Status)>
            {
                ("Logger", true, "Injected successfully - validated in constructor"),
                ("Configuration", true, "Injected successfully - validated in constructor"),
            };

            // Test actual functionality of dependencies
            var loggerWorks = false;
            var configWorks = false;

            try
            {
                _logger.LogTrace("Dependency injection health check - logger test");
                loggerWorks = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Logger dependency health check failed");
            }

            try
            {
                // Access a property on configuration to validate it's properly constructed
                var _ = _configuration.Force_Service_ID; // Safe to access, validates object integrity
                configWorks = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Configuration dependency health check failed");
            }

            var allHealthy = dependencyHealthChecks.All(h => h.IsHealthy) && loggerWorks && configWorks;

            var response = new DependencyTestResponse
            {
                AllDependenciesHealthy = allHealthy,
                DependencyChecks = dependencyHealthChecks,
                LoggerFunctional = loggerWorks,
                ConfigurationFunctional = configWorks,
                Message = allHealthy ? "All dependencies healthy" : "Some dependencies failed health check",
                Timestamp = DateTime.UtcNow
            };

            return Task.FromResult<(StatusCodes, DependencyTestResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error testing dependency injection health");
            return Task.FromResult<(StatusCodes, DependencyTestResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    // IDaprService lifecycle methods are provided by default interface implementations
    // No need to override unless custom logic is required beyond the default logging
}

/// <summary>
/// Testing service interface for dependency injection.
/// </summary>
public interface ITestingService : IDaprService
{
    Task<(StatusCodes, TestResponse?)> RunTestAsync(string testName, CancellationToken cancellationToken = default);
    Task<(StatusCodes, ConfigTestResponse?)> TestConfigurationAsync(CancellationToken cancellationToken = default);
    Task<(StatusCodes, DependencyTestResponse?)> TestDependencyInjectionHealthAsync(CancellationToken cancellationToken = default);
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
/// Response model for dependency injection health tests.
/// </summary>
public class DependencyTestResponse
{
    public bool AllDependenciesHealthy { get; set; }
    public List<(string Name, bool IsHealthy, string Status)> DependencyChecks { get; set; } = new();
    public bool LoggerFunctional { get; set; }
    public bool ConfigurationFunctional { get; set; }
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
