using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Generic background worker that periodically flushes one or more
/// <see cref="IFlushable"/> event batchers. Follows the canonical BackgroundService
/// polling loop pattern with configurable startup delay, double-catch cancellation
/// filter, per-cycle telemetry, and WorkerErrorPublisher.
/// </summary>
/// <remarks>
/// <para>
/// A single worker can flush multiple batchers per cycle via the <c>IFlushable[]</c>
/// constructor parameter. This allows services like Item — which needs three batchers
/// (Mode 1 created, Mode 2 modified, Mode 1 destroyed) — to use one BackgroundService
/// instead of three.
/// </para>
/// <para>
/// <b>FOUNDATION TENETS - Background Service Pattern:</b>
/// Follows the canonical BackgroundService polling loop with configurable startup delay,
/// double-catch cancellation filter, per-cycle telemetry, and WorkerErrorPublisher.
/// </para>
/// </remarks>
public class EventBatcherWorker : BackgroundService
{
    private readonly IFlushable[] _batchers;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBatcherWorker> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly int _intervalSeconds;
    private readonly int _startupDelaySeconds;
    private readonly string _serviceName;
    private readonly string _workerName;

    /// <summary>
    /// Initializes the event batcher worker.
    /// </summary>
    /// <param name="batchers">One or more flushable batchers to drain per cycle.</param>
    /// <param name="serviceProvider">Service provider for WorkerErrorPublisher scope creation.</param>
    /// <param name="logger">Logger for lifecycle and error messages.</param>
    /// <param name="telemetryProvider">Telemetry provider for per-cycle span instrumentation.</param>
    /// <param name="intervalSeconds">Seconds between flush cycles.</param>
    /// <param name="startupDelaySeconds">Seconds to wait before the first flush cycle.</param>
    /// <param name="serviceName">Logical service name for telemetry and error events (e.g., "item").</param>
    /// <param name="workerName">Worker name for telemetry and error events (e.g., "InstanceEventBatcher").</param>
    public EventBatcherWorker(
        IFlushable[] batchers,
        IServiceProvider serviceProvider,
        ILogger<EventBatcherWorker> logger,
        ITelemetryProvider telemetryProvider,
        int intervalSeconds,
        int startupDelaySeconds,
        string serviceName,
        string workerName)
    {
        _batchers = batchers;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _intervalSeconds = intervalSeconds;
        _startupDelaySeconds = startupDelaySeconds;
        _serviceName = serviceName;
        _workerName = workerName;
    }

    /// <summary>
    /// Main execution loop. Waits for startup delay, then periodically flushes
    /// all registered batchers.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_startupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s, batchers: {Count}",
            _workerName, _intervalSeconds, _batchers.Length);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    $"bannou.{_serviceName}", $"{_workerName}.ProcessCycle");

                foreach (var batcher in _batchers)
                {
                    if (!batcher.IsEmpty)
                    {
                        await batcher.FlushAsync(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", _workerName);
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    _serviceName, _workerName, ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_intervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        // Best-effort final flush on shutdown
        foreach (var batcher in _batchers)
        {
            try
            {
                if (!batcher.IsEmpty)
                {
                    await batcher.FlushAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Worker} final flush failed for {Batcher}",
                    _workerName, batcher.GetType().Name);
            }
        }

        _logger.LogInformation("{Worker} stopped", _workerName);
    }
}
