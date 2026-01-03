using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Background service that periodically syncs the GameSessionService session caches
/// with the authoritative source (Connect service) to ensure consistency across instances.
/// This catches any missed session.connected/disconnected events that could cause cache divergence.
/// </summary>
public class SessionCacheSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCacheSyncService> _logger;

    /// <summary>
    /// Interval between cache sync operations (default: 5 minutes).
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initial delay before first sync to allow services to start.
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    public SessionCacheSyncService(
        IServiceProvider serviceProvider,
        ILogger<SessionCacheSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session cache sync service starting, sync interval: {Interval}", SyncInterval);

        // Wait before first sync to allow other services to start
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Session cache sync service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncCachesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cache sync");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetService<IMessageBus>();
                    if (messageBus != null)
                    {
                        await messageBus.TryPublishErrorAsync(
                            "game-session",
                            "SessionCacheSync",
                            ex.GetType().Name,
                            ex.Message,
                            severity: BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity.Warning);
                    }
                }
                catch
                {
                    // Don't let error publishing failures affect the loop
                }
            }

            try
            {
                await Task.Delay(SyncInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Session cache sync service stopped");
    }

    /// <summary>
    /// Syncs the subscription cache by refreshing subscription data for all tracked accounts.
    /// For session caches, we rely on Connect service's authoritative index and will
    /// add account-based lookup when the IConnectClient is available.
    /// </summary>
    private async Task SyncCachesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting session cache sync");

        using var scope = _serviceProvider.CreateScope();

        // Get GameSessionService to access its sync method
        var gameSessionService = scope.ServiceProvider.GetService<IGameSessionService>() as GameSessionService;
        if (gameSessionService == null)
        {
            _logger.LogWarning("GameSessionService not available for cache sync");
            return;
        }

        // Sync subscription caches
        var syncedCount = await gameSessionService.SyncSubscriptionCachesAsync(cancellationToken);
        _logger.LogInformation("Synced subscription data for {Count} accounts", syncedCount);
    }
}
