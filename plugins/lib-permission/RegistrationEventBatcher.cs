using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Background worker that accumulates service registration events and publishes
/// them as bulk observability events on a configurable interval.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// The ConcurrentDictionary accumulator is in-memory per-node. Each node publishes
/// its own batch independently. Analytics aggregates per-node data. This is by-design
/// for observability — registration events are fire-and-forget with no functional dependency.
/// </para>
/// <para>
/// <b>FOUNDATION TENETS - Background Service Pattern:</b>
/// Follows the canonical BackgroundService polling loop with configurable startup delay,
/// double-catch cancellation filter, per-cycle telemetry, and WorkerErrorPublisher.
/// </para>
/// </remarks>
public class RegistrationEventBatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegistrationEventBatcher> _logger;
    private readonly PermissionServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private ConcurrentDictionary<string, PermissionRegistrationEntry> _pendingRegistrations = new();
    private DateTimeOffset _windowStartedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initializes the registration event batcher with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped IMessageBus.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with batch interval and startup delay.</param>
    /// <param name="telemetryProvider">Telemetry provider for per-cycle span instrumentation.</param>
    public RegistrationEventBatcher(
        IServiceProvider serviceProvider,
        ILogger<RegistrationEventBatcher> logger,
        PermissionServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Records a service registration for inclusion in the next bulk publish.
    /// Deduplicates by serviceId — last registration wins within a batch window.
    /// </summary>
    /// <param name="serviceId">The service identifier that registered.</param>
    /// <param name="version">The service version at registration time.</param>
    internal void Add(string serviceId, string? version)
    {
        _pendingRegistrations[serviceId] = new PermissionRegistrationEntry
        {
            ServiceId = serviceId,
            Version = version,
            RegisteredAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Main execution loop. Waits for startup delay, then periodically flushes
    /// accumulated registrations as bulk events.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.RegistrationBatchStartupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s",
            nameof(RegistrationEventBatcher), _configuration.RegistrationBatchIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.permission", "RegistrationEventBatcher.ProcessCycle");
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(RegistrationEventBatcher));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "permission", "RegistrationEventBatcher", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.RegistrationBatchIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        // Best-effort final flush on shutdown
        try { await FlushAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Worker} final flush failed", nameof(RegistrationEventBatcher));
        }

        _logger.LogInformation("{Worker} stopped", nameof(RegistrationEventBatcher));
    }

    /// <summary>
    /// Atomically swaps the pending registrations dictionary with a fresh one,
    /// then publishes all accumulated entries as a single bulk event.
    /// </summary>
    private async Task FlushAsync(CancellationToken ct)
    {
        if (_pendingRegistrations.IsEmpty) return;

        var snapshot = Interlocked.Exchange(
            ref _pendingRegistrations,
            new ConcurrentDictionary<string, PermissionRegistrationEntry>());

        if (snapshot.IsEmpty) return;

        var entries = snapshot.Values.ToList();
        var windowStart = _windowStartedAt;
        _windowStartedAt = DateTimeOffset.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var evt = new PermissionServicesRegistered
        {
            Registrations = entries,
            RegistrationCount = entries.Count,
            WindowStartedAt = windowStart
        };

        await messageBus.PublishPermissionServicesRegisteredAsync(evt, ct);

        _logger.LogInformation(
            "Published bulk registration event with {Count} services: [{ServiceIds}]",
            entries.Count,
            string.Join(", ", entries.Select(e => e.ServiceId)));
    }
}
