using System.Runtime.CompilerServices;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Orchestrator;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using LibOrchestrator;

[assembly: InternalsVisibleTo("lib-orchestrator.tests")]

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Implementation of the Orchestrator service.
/// This class contains the business logic for all Orchestrator operations.
/// CRITICAL: Uses direct Redis/RabbitMQ connections (NOT Dapr) to avoid chicken-and-egg dependency.
/// </summary>
[DaprService("orchestrator", typeof(IOrchestratorService), lifetime: ServiceLifetime.Scoped)]
public class OrchestratorService : IOrchestratorService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<OrchestratorService> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly OrchestratorRedisManager _redisManager;
    private readonly OrchestratorEventManager _eventManager;
    private readonly ServiceHealthMonitor _healthMonitor;
    private readonly SmartRestartManager _restartManager;

    private const string STATE_STORE = "orchestrator-store";

    public OrchestratorService(
        DaprClient daprClient,
        ILogger<OrchestratorService> logger,
        OrchestratorServiceConfiguration configuration,
        OrchestratorRedisManager redisManager,
        OrchestratorEventManager eventManager,
        ServiceHealthMonitor healthMonitor,
        SmartRestartManager restartManager)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _redisManager = redisManager ?? throw new ArgumentNullException(nameof(redisManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _restartManager = restartManager ?? throw new ArgumentNullException(nameof(restartManager));
    }

    /// <summary>
    /// Implementation of GetInfrastructureHealth operation.
    /// Validates connectivity and health of core infrastructure components.
    /// </summary>
    public async Task<(StatusCodes, InfrastructureHealthResponse?)> GetInfrastructureHealthAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetInfrastructureHealth operation");

        try
        {
            var components = new List<ComponentHealth>();
            var overallHealthy = true;

            // Check Redis connectivity
            var (redisHealthy, redisMessage, redisPing) = await _redisManager.CheckHealthAsync();
            components.Add(new ComponentHealth
            {
                Name = "redis",
                Status = redisHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = redisMessage ?? string.Empty,
                Metrics = redisPing.HasValue
                    ? (object)new Dictionary<string, object> { { "pingTimeMs", redisPing.Value.TotalMilliseconds } }
                    : new Dictionary<string, object>()
            });
            overallHealthy = overallHealthy && redisHealthy;

            // Check RabbitMQ connectivity
            var (rabbitHealthy, rabbitMessage) = _eventManager.CheckHealth();
            components.Add(new ComponentHealth
            {
                Name = "rabbitmq",
                Status = rabbitHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = rabbitMessage ?? string.Empty
            });
            overallHealthy = overallHealthy && rabbitHealthy;

            // Check Dapr Placement service (via DaprClient)
            try
            {
                // Simple health check via Dapr metadata endpoint
                await _daprClient.CheckHealthAsync(cancellationToken);
                components.Add(new ComponentHealth
                {
                    Name = "placement",
                    Status = ComponentHealthStatus.Healthy,
                    LastSeen = DateTimeOffset.UtcNow,
                    Message = "Dapr sidecar responding"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dapr health check failed");
                components.Add(new ComponentHealth
                {
                    Name = "placement",
                    Status = ComponentHealthStatus.Unavailable,
                    LastSeen = DateTimeOffset.UtcNow,
                    Message = $"Dapr check failed: {ex.Message}"
                });
                overallHealthy = false;
            }

            var response = new InfrastructureHealthResponse
            {
                Healthy = overallHealthy,
                Timestamp = DateTimeOffset.UtcNow,
                Components = components
            };

            var statusCode = overallHealthy ? StatusCodes.OK : StatusCodes.InternalServerError;
            return (statusCode, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetInfrastructureHealth operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetServicesHealth operation.
    /// Retrieves health information from all services via Redis heartbeat monitoring.
    /// </summary>
    public async Task<(StatusCodes, ServiceHealthReport?)> GetServicesHealthAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetServicesHealth operation");

        try
        {
            var report = await _healthMonitor.GetServiceHealthReportAsync();
            return (StatusCodes.OK, report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetServicesHealth operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RunTests operation.
    /// Executes tests via API calls to test services (no --exit-code-from).
    /// </summary>
    public Task<(StatusCodes, TestExecutionResult?)> RunTestsAsync(TestExecutionRequest body, CancellationToken cancellationToken)
    {
        var testTypeStr = body.TestType.ToString().ToLowerInvariant();
        _logger.LogInformation(
            "Executing RunTests operation: {TestType} (plugin: {Plugin})",
            testTypeStr, body.Plugin ?? "all");

        try
        {
            var startTime = DateTime.UtcNow;

            // TODO: Implement test execution via API calls to test services
            // This would involve:
            // 1. For infrastructure tests: Call testing service health check APIs
            // 2. For http tests: Call http-tester service with plugin parameter
            // 3. For edge tests: Call edge-tester service for WebSocket protocol validation

            _logger.LogWarning("Test execution via API not yet fully implemented");

            // Placeholder implementation
            var result = new TestExecutionResult
            {
                Success = false,
                TestType = testTypeStr,
                Duration = (DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss"),
                Results = new List<TestResult>(),
                Summary = "Test execution via API not yet implemented",
                Error = "Full implementation requires integration with http-tester and edge-tester services"
            };

            return Task.FromResult<(StatusCodes, TestExecutionResult?)>((StatusCodes.BadRequest, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RunTests operation");
            return Task.FromResult<(StatusCodes, TestExecutionResult?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Implementation of RestartService operation.
    /// Performs intelligent service restart based on health metrics.
    /// </summary>
    public async Task<(StatusCodes, ServiceRestartResult?)> RestartServiceAsync(ServiceRestartRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing RestartService operation for: {ServiceName} (force: {Force})",
            body.ServiceName, body.Force);

        try
        {
            var result = await _restartManager.RestartServiceAsync(body);

            if (!result.Success)
            {
                // Check if restart was declined (healthy service)
                if (result.Message?.Contains("not needed") == true)
                {
                    return (StatusCodes.Conflict, result);
                }

                // Restart failed
                return (StatusCodes.InternalServerError, result);
            }

            return (StatusCodes.OK, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RestartService operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ShouldRestartService operation.
    /// Evaluates service health and determines if restart is necessary.
    /// </summary>
    public async Task<(StatusCodes, RestartRecommendation?)> ShouldRestartServiceAsync(ShouldRestartServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ShouldRestartService operation for: {ServiceName}", body.ServiceName);

        try
        {
            var recommendation = await _healthMonitor.ShouldRestartServiceAsync(body.ServiceName);
            return (StatusCodes.OK, recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ShouldRestartService operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

}
