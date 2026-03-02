using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Background service that processes matchmaking queues at configured intervals.
/// Runs the interval-based match processing loop that:
/// - Increments ticket intervals
/// - Tries to form matches with expanded skill windows
/// - Handles timeout behavior for tickets exceeding max intervals
/// - Publishes matchmaking stats events
/// </summary>
public class MatchmakingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MatchmakingServiceConfiguration _configuration;
    private readonly ILogger<MatchmakingBackgroundService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new MatchmakingBackgroundService instance.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes.</param>
    /// <param name="configuration">Matchmaking service configuration.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public MatchmakingBackgroundService(
        IServiceProvider serviceProvider,
        MatchmakingServiceConfiguration configuration,
        ILogger<MatchmakingBackgroundService> logger,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Executes the background processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingBackgroundService.ExecuteAsync");
        // Wait for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(_configuration.BackgroundServiceStartupDelaySeconds), stoppingToken);

        _logger.LogInformation(
            "MatchmakingBackgroundService starting with interval of {IntervalSeconds} seconds",
            _configuration.ProcessingIntervalSeconds);

        var interval = TimeSpan.FromSeconds(_configuration.ProcessingIntervalSeconds);
        var statsInterval = TimeSpan.FromSeconds(_configuration.StatsPublishIntervalSeconds);
        var lastStatsPublish = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Create a scope for the scoped MatchmakingService
                using (var scope = _serviceProvider.CreateScope())
                {
                    var matchmakingService = scope.ServiceProvider.GetRequiredService<IMatchmakingService>();

                    if (matchmakingService is MatchmakingService service)
                    {
                        await service.ProcessAllQueuesAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("MatchmakingService is not of expected type, skipping interval processing");
                    }
                }

                // Publish stats periodically
                if (DateTimeOffset.UtcNow - lastStatsPublish >= statsInterval)
                {
                    await PublishStatsAsync(stoppingToken);
                    lastStatsPublish = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during matchmaking interval processing");
            }

            // Wait for next interval
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("MatchmakingBackgroundService stopped");
    }

    /// <summary>
    /// Publishes matchmaking statistics event.
    /// </summary>
    private async Task PublishStatsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingBackgroundService.PublishStatsAsync");
        try
        {
            // Create a scope for the scoped MatchmakingService
            using var scope = _serviceProvider.CreateScope();
            var matchmakingService = scope.ServiceProvider.GetRequiredService<IMatchmakingService>();

            if (matchmakingService is MatchmakingService service)
            {
                await service.PublishStatsAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish matchmaking stats");
        }
    }
}
