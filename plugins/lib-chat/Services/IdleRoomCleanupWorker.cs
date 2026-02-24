using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Background service that periodically cleans up idle chat rooms.
/// Rooms that have no activity for longer than the configured timeout are archived (persistent)
/// or deleted (ephemeral).
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services.
/// Follows established patterns from BanExpiryWorker (distributed lock + batch cleanup).
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// Acquires a distributed lock per cycle to prevent multiple instances from
/// processing the same idle rooms concurrently.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses IdleRoomCleanupIntervalMinutes, IdleRoomTimeoutMinutes,
/// IdleRoomCleanupStartupDelaySeconds, and IdleRoomCleanupLockExpirySeconds
/// from ChatServiceConfiguration.
/// </para>
/// </remarks>
public class IdleRoomCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IdleRoomCleanupWorker> _logger;
    private readonly ChatServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Interval between cleanup cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromMinutes(_configuration.IdleRoomCleanupIntervalMinutes);

    /// <summary>
    /// Initializes the idle room cleanup worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with cleanup settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public IdleRoomCleanupWorker(
        IServiceProvider serviceProvider,
        ILogger<IdleRoomCleanupWorker> logger,
        ChatServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Runs on a configurable interval and delegates actual cleanup to ChatService.CleanupIdleRoomsAsync.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Idle room cleanup worker starting, interval: {Interval} minutes, timeout: {Timeout} minutes",
            _configuration.IdleRoomCleanupIntervalMinutes,
            _configuration.IdleRoomTimeoutMinutes);

        // Initial delay before first cycle to let the system stabilize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.IdleRoomCleanupStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Idle room cleanup worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCleanupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during idle room cleanup cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "chat",
                        "IdleRoomCleanupWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing cleanup loop");
                }
            }

            try
            {
                await Task.Delay(WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Idle room cleanup worker stopped");
    }

    /// <summary>
    /// Processes one cleanup cycle by resolving a scoped ChatService and delegating to it.
    /// </summary>
    private async Task ProcessCleanupCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "IdleRoomCleanupWorker.ProcessCleanupCycle");

        _logger.LogDebug("Starting idle room cleanup cycle");

        using var scope = _serviceProvider.CreateScope();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Distributed lock prevents multiple instances from processing the same cycle concurrently.
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock,
            "idle-room-cleanup",
            Guid.NewGuid().ToString(),
            _configuration.IdleRoomCleanupLockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire idle room cleanup lock, another instance is processing this cycle");
            return;
        }

        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        if (chatService is not ChatService service)
        {
            _logger.LogError("Failed to resolve ChatService for cleanup cycle");
            return;
        }

        var result = await service.CleanupIdleRoomsAsync(cancellationToken);

        _logger.LogInformation(
            "Idle room cleanup cycle complete: archived={Archived}, deleted={Deleted}",
            result.ArchivedRooms,
            result.DeletedRooms);
    }
}
