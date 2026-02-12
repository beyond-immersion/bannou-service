using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Background worker that periodically evaluates active scenario instances for timeout,
/// abandonment detection, and POI expiration. Runs at a configurable interval
/// defined by <see cref="GardenerServiceConfiguration.ScenarioLifecycleWorkerIntervalSeconds"/>.
/// </summary>
public class GardenerScenarioLifecycleWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GardenerServiceConfiguration _configuration;
    private readonly ILogger<GardenerScenarioLifecycleWorker> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GardenerScenarioLifecycleWorker"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped instances.</param>
    /// <param name="configuration">Gardener service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public GardenerScenarioLifecycleWorker(
        IServiceProvider serviceProvider,
        GardenerServiceConfiguration configuration,
        ILogger<GardenerScenarioLifecycleWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(
            TimeSpan.FromSeconds(_configuration.BackgroundServiceStartupDelaySeconds),
            stoppingToken);

        _logger.LogInformation(
            "GardenerScenarioLifecycleWorker starting with interval {IntervalSeconds}s",
            _configuration.ScenarioLifecycleWorkerIntervalSeconds);

        var interval = TimeSpan.FromSeconds(_configuration.ScenarioLifecycleWorkerIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var gardenerService = scope.ServiceProvider.GetRequiredService<IGardenerService>();

                if (gardenerService is GardenerService service)
                {
                    await service.ProcessScenarioLifecycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scenario lifecycle processing");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("GardenerScenarioLifecycleWorker stopped");
    }
}
