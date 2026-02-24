using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Background worker that scans the typing:active sorted set for expired entries
/// and publishes ChatTypingStoppedClientEvent for each.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services.
/// Follows established patterns from IdleRoomCleanupWorker.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses TypingWorkerIntervalMilliseconds, TypingTimeoutSeconds, and TypingWorkerBatchSize
/// from ChatServiceConfiguration.
/// </para>
/// </remarks>
public class TypingExpiryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TypingExpiryWorker> _logger;
    private readonly ChatServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes the typing expiry worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with typing timeout settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public TypingExpiryWorker(
        IServiceProvider serviceProvider,
        ILogger<TypingExpiryWorker> logger,
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
    /// Runs on a configurable interval, scanning for expired typing entries.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Typing expiry worker started with {IntervalMs}ms interval and {TimeoutSec}s timeout",
            _configuration.TypingWorkerIntervalMilliseconds,
            _configuration.TypingTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredTypingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during typing expiry cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "chat",
                        "TypingExpiryWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing typing expiry loop");
                }
            }

            try
            {
                await Task.Delay(_configuration.TypingWorkerIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Typing expiry worker stopped");
    }

    /// <summary>
    /// Processes one expiry cycle: queries the sorted set for entries older than
    /// the typing timeout, removes them, and publishes stop events.
    /// </summary>
    private async Task ProcessExpiredTypingAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "TypingExpiryWorker.ProcessExpiredTyping");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var entitySessionRegistry = scope.ServiceProvider.GetRequiredService<IEntitySessionRegistry>();
        var participantStore = stateStoreFactory.GetCacheableStore<ChatParticipantModel>(
            StateStoreDefinitions.ChatParticipants);

        var cutoffMs = (double)DateTimeOffset.UtcNow
            .AddSeconds(-_configuration.TypingTimeoutSeconds)
            .ToUnixTimeMilliseconds();

        var expired = await participantStore.SortedSetRangeByScoreAsync(
            "typing:active", double.NegativeInfinity, cutoffMs,
            count: _configuration.TypingWorkerBatchSize);

        foreach (var (member, _) in expired)
        {
            // Parse "{roomId:N}:{sessionId:N}" -- separator at fixed index 32
            if (member.Length != 65) continue; // 32 + 1 + 32

            var roomIdStr = member[..32];
            var sessionIdStr = member[33..];

            if (!Guid.TryParse(roomIdStr, out var roomId) ||
                !Guid.TryParse(sessionIdStr, out var sessionId))
                continue;

            await participantStore.SortedSetRemoveAsync("typing:active", member, ct);

            _logger.LogDebug("Typing expired for session {SessionId} in room {RoomId}",
                sessionId, roomId);

            // Publish stop event via entity session registry -- no participant lookup needed
            await entitySessionRegistry.PublishToEntitySessionsAsync(
                "chat-room", roomId,
                new ChatTypingStoppedClientEvent
                {
                    RoomId = roomId,
                    ParticipantSessionId = sessionId,
                }, ct);
        }
    }
}
