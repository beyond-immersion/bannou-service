using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Testing service implementation that exercises the plugin system properly.
/// Implements IBannouService to test centralized service resolution.
/// </summary>
[BannouService("testing", interfaceType: typeof(ITestingService), priority: false, lifetime: ServiceLifetime.Scoped)]
public partial class TestingService : ITestingService
{
    private readonly ILogger<TestingService> _logger;
    private readonly TestingServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;

    public TestingService(
        ILogger<TestingService> logger,
        TestingServiceConfiguration configuration,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IEventConsumer eventConsumer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _clientEventPublisher = clientEventPublisher ?? throw new ArgumentNullException(nameof(clientEventPublisher));

        // Required by Tenet 6 - calls default IBannouService.RegisterEventConsumers() no-op
        // Must cast to interface to access default interface implementation
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Simple test method to verify service is working.
    /// </summary>
    public async Task<(StatusCodes, TestResponse?)> RunTestAsync(string testName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Running test: {TestName}", testName);

            var response = new TestResponse
            {
                TestName = testName,
                Success = true,
                Message = $"Test {testName} completed successfully",
                Timestamp = DateTime.UtcNow
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running test: {TestName}", testName);
            await _messageBus.TryPublishErrorAsync(
                "testing",
                "RunTest",
                ex.GetType().Name,
                ex.Message,
                dependency: "testing",
                endpoint: "post:/testing/run",
                details: new { TestName = testName },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Test method to verify configuration is working.
    /// </summary>
    public async Task<(StatusCodes, ConfigTestResponse?)> TestConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Testing configuration");

            var response = new ConfigTestResponse
            {
                ConfigLoaded = _configuration != null,
                Message = "Configuration test completed",
                Timestamp = DateTime.UtcNow
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing configuration");
            await _messageBus.TryPublishErrorAsync(
                "testing",
                "TestConfiguration",
                ex.GetType().Name,
                ex.Message,
                dependency: "testing",
                endpoint: "post:/testing/config",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Test method to validate dependency injection health and null safety patterns.
    /// This test validates the fixes for null-forgiving operators and constructor issues.
    /// </summary>
    public async Task<(StatusCodes, DependencyTestResponse?)> TestDependencyInjectionHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Testing dependency injection health");

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

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing dependency injection health");
            await _messageBus.TryPublishErrorAsync(
                "testing",
                "TestDependencyInjectionHealth",
                ex.GetType().Name,
                ex.Message,
                dependency: "testing",
                endpoint: "post:/testing/dependency-health",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Ping / Latency Testing

    /// <summary>
    /// Ping endpoint for measuring round-trip latency from game clients.
    /// Echoes client timestamp and provides server timing for RTT calculation.
    /// </summary>
    /// <param name="request">Optional ping request with client timestamp and sequence</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ping response with timing information</returns>
    public async Task<(StatusCodes, PingResponse?)> PingAsync(
        PingRequest? request,
        CancellationToken cancellationToken = default)
    {
        var serverReceiveTime = DateTimeOffset.UtcNow;

        try
        {
            // Create response with timing data
            var response = new PingResponse
            {
                ServerTimestamp = serverReceiveTime.ToUnixTimeMilliseconds(),
                ClientTimestamp = request?.ClientTimestamp,
                Sequence = request?.Sequence ?? 0,
                ServerProcessingTimeMs = 0 // Will be calculated at the end
            };

            // Calculate processing time
            var processingTime = DateTimeOffset.UtcNow - serverReceiveTime;
            response.ServerProcessingTimeMs = processingTime.TotalMilliseconds;

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ping request");
            await _messageBus.TryPublishErrorAsync(
                "testing",
                "Ping",
                ex.GetType().Name,
                ex.Message,
                dependency: "testing",
                endpoint: "post:/testing/ping",
                details: new { Sequence = request?.Sequence },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Client Event Testing

    /// <summary>
    /// Publishes a test notification event to a specific session.
    /// Used for testing the client event delivery system.
    /// </summary>
    /// <param name="sessionId">The session ID to send the event to</param>
    /// <param name="message">The notification message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Status code and response indicating success or failure</returns>
    public async Task<(StatusCodes, PublishTestEventResponse?)> PublishTestEventAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return (StatusCodes.BadRequest, new PublishTestEventResponse
            {
                Success = false,
                Message = "Session ID is required",
                Timestamp = DateTime.UtcNow
            });
        }

        try
        {
            _logger.LogInformation("Publishing test notification event to session {SessionId}", sessionId);

            var testEvent = new SystemNotificationEvent
            {
                Event_name = SystemNotificationEventEvent_name.System_notification,
                Event_id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Notification_type = SystemNotificationEventNotification_type.Info,
                Title = "Test Notification",
                Message = message ?? "This is a test notification from the Testing service"
            };

            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId, testEvent, cancellationToken);

            if (published)
            {
                _logger.LogInformation("Successfully published test event to session {SessionId}", sessionId);
                return (StatusCodes.OK, new PublishTestEventResponse
                {
                    Success = true,
                    Message = "Test event published successfully",
                    EventId = testEvent.Event_id,
                    SessionId = sessionId,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                _logger.LogWarning("Failed to publish test event to session {SessionId}", sessionId);
                return (StatusCodes.InternalServerError, new PublishTestEventResponse
                {
                    Success = false,
                    Message = "Failed to publish test event",
                    SessionId = sessionId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing test event to session {SessionId}", sessionId);
            return (StatusCodes.InternalServerError, new PublishTestEventResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                SessionId = sessionId,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Unlike other services which use generated permission registration, Testing service
    /// uses a manually maintained registration since there's no testing-api.yaml schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Testing service permissions...");
        await TestingPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}

/// <summary>
/// Testing service interface for dependency injection.
/// </summary>
public interface ITestingService : IBannouService
{
    Task<(StatusCodes, TestResponse?)> RunTestAsync(string testName, CancellationToken cancellationToken = default);
    Task<(StatusCodes, ConfigTestResponse?)> TestConfigurationAsync(CancellationToken cancellationToken = default);
    Task<(StatusCodes, DependencyTestResponse?)> TestDependencyInjectionHealthAsync(CancellationToken cancellationToken = default);
    Task<(StatusCodes, PingResponse?)> PingAsync(PingRequest? request, CancellationToken cancellationToken = default);
    Task<(StatusCodes, PublishTestEventResponse?)> PublishTestEventAsync(string sessionId, string message, CancellationToken cancellationToken = default);
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
/// Response model for publishing test events to client sessions.
/// </summary>
public class PublishTestEventResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? EventId { get; set; }
    public string? SessionId { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Request model for ping/latency testing.
/// All fields are optional - a minimal ping can be sent with empty body.
/// </summary>
public class PingRequest
{
    /// <summary>
    /// Client's Unix timestamp in milliseconds when the request was sent.
    /// Echoed back in response for client-side RTT calculation.
    /// </summary>
    public long? ClientTimestamp { get; set; }

    /// <summary>
    /// Sequence number for tracking in sustained ping tests.
    /// Useful for detecting packet loss or reordering.
    /// </summary>
    public int Sequence { get; set; }
}

/// <summary>
/// Response model for ping/latency testing.
/// Provides all timing information needed for latency analysis.
/// </summary>
public class PingResponse
{
    /// <summary>
    /// Server's Unix timestamp in milliseconds when the request was received.
    /// Can be compared with client timestamp to detect clock skew.
    /// </summary>
    public long ServerTimestamp { get; set; }

    /// <summary>
    /// Client's timestamp echoed back (if provided in request).
    /// Client can calculate RTT as: (response_received_time - client_timestamp).
    /// </summary>
    public long? ClientTimestamp { get; set; }

    /// <summary>
    /// Sequence number echoed back from request.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Time in milliseconds the server spent processing this request.
    /// Network latency = (RTT - ServerProcessingTimeMs) / 2.
    /// </summary>
    public double ServerProcessingTimeMs { get; set; }
}
