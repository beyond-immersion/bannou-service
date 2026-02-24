using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Background service that periodically deletes persistent (MySQL) messages older than
/// the room type's configured <see cref="ChatRoomTypeModel.RetentionDays"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services.
/// Follows established patterns from BanExpiryWorker (distributed lock + batch deletion).
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// Acquires a distributed lock per cycle to prevent multiple instances from
/// processing the same expired messages concurrently.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses MessageRetentionCleanupIntervalMinutes, MessageRetentionStartupDelaySeconds,
/// MessageRetentionBatchSize, and MessageRetentionLockExpirySeconds from ChatServiceConfiguration.
/// </para>
/// </remarks>
public class MessageRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageRetentionWorker> _logger;
    private readonly ChatServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Interval between retention cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromMinutes(_configuration.MessageRetentionCleanupIntervalMinutes);

    /// <summary>
    /// Initializes the message retention worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with retention cleanup settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public MessageRetentionWorker(
        IServiceProvider serviceProvider,
        ILogger<MessageRetentionWorker> logger,
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
    /// Runs on a configurable interval and scans for messages past their retention period.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Message retention worker starting, interval: {Interval} minutes, batch size: {BatchSize}",
            _configuration.MessageRetentionCleanupIntervalMinutes,
            _configuration.MessageRetentionBatchSize);

        // Initial delay before first cycle to let the system stabilize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.MessageRetentionStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Message retention worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRetentionCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message retention cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "chat",
                        "MessageRetentionWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing retention loop");
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

        _logger.LogInformation("Message retention worker stopped");
    }

    /// <summary>
    /// Processes one retention cycle: acquires a distributed lock, iterates room types
    /// with retention configured, and deletes expired messages per room.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessRetentionCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "MessageRetentionWorker.ProcessRetentionCycle");

        _logger.LogDebug("Starting message retention cycle");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Distributed lock prevents multiple instances from processing the same cycle concurrently.
        // Uses dedicated lock expiry (default 300s) because iterating room types, rooms, and messages
        // may take significantly longer than a single per-room mutation.
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock,
            "message-retention-cleanup",
            Guid.NewGuid().ToString(),
            _configuration.MessageRetentionLockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire message retention lock, another instance is processing this cycle");
            return;
        }

        var roomTypeStore = stateStoreFactory.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes);
        var roomStore = stateStoreFactory.GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms);
        var messageStore = stateStoreFactory.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages);

        // Find all room types with retention configured and persistent storage
        var roomTypes = await GetRoomTypesWithRetentionAsync(roomTypeStore, cancellationToken);
        if (roomTypes.Count == 0)
        {
            _logger.LogDebug("No room types with retention configured");
            return;
        }

        var totalDeleted = 0;

        foreach (var roomType in roomTypes)
        {
            var retentionDays = roomType.RetentionDays ?? throw new InvalidOperationException(
                $"Room type '{roomType.Code}' passed retention filter but RetentionDays is null");

            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var deleted = await DeleteExpiredMessagesForRoomTypeAsync(
                roomType, cutoff, roomStore, messageStore, cancellationToken);
            totalDeleted += deleted;
        }

        _logger.LogInformation(
            "Message retention cycle complete: deleted {DeletedCount} expired messages across {RoomTypeCount} room types",
            totalDeleted, roomTypes.Count);
    }

    /// <summary>
    /// Queries room types that have both RetentionDays set and Persistent storage mode.
    /// </summary>
    private async Task<IReadOnlyList<ChatRoomTypeModel>> GetRoomTypesWithRetentionAsync(
        IJsonQueryableStateStore<ChatRoomTypeModel> roomTypeStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "MessageRetentionWorker.GetRoomTypesWithRetention");

        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.RetentionDays", Operator = QueryOperator.Exists, Value = true },
            new() { Path = "$.PersistenceMode", Operator = QueryOperator.Equals, Value = PersistenceMode.Persistent },
            new() { Path = "$.Status", Operator = QueryOperator.Equals, Value = RoomTypeStatus.Active },
        };

        var result = await roomTypeStore.JsonQueryPagedAsync(conditions, 0, _configuration.MessageRetentionMaxRoomTypeResults, cancellationToken: cancellationToken);
        return result.Items.Select(r => r.Value).ToList();
    }

    /// <summary>
    /// Deletes expired messages for all rooms of the given room type.
    /// </summary>
    /// <returns>Total number of messages deleted.</returns>
    private async Task<int> DeleteExpiredMessagesForRoomTypeAsync(
        ChatRoomTypeModel roomType,
        DateTimeOffset cutoff,
        IJsonQueryableStateStore<ChatRoomModel> roomStore,
        IJsonQueryableStateStore<ChatMessageModel> messageStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "MessageRetentionWorker.DeleteExpiredMessagesForRoomType");

        // Find all rooms of this type
        var roomConditions = new List<QueryCondition>
        {
            new() { Path = "$.RoomTypeCode", Operator = QueryOperator.Equals, Value = roomType.Code },
        };

        var rooms = await roomStore.JsonQueryPagedAsync(roomConditions, 0, _configuration.MessageRetentionMaxRoomsPerType, cancellationToken: cancellationToken);
        var totalDeleted = 0;

        foreach (var roomItem in rooms.Items)
        {
            var room = roomItem.Value;
            var deleted = await DeleteExpiredMessagesForRoomAsync(
                room.RoomId, cutoff, messageStore, cancellationToken);
            totalDeleted += deleted;
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Deleted {DeletedCount} expired messages for room type {RoomTypeCode} (retention: {RetentionDays} days, rooms: {RoomCount})",
                totalDeleted, roomType.Code, roomType.RetentionDays, rooms.Items.Count);
        }

        return totalDeleted;
    }

    /// <summary>
    /// Deletes messages in a single room that are older than the cutoff timestamp.
    /// Processes up to <see cref="ChatServiceConfiguration.MessageRetentionBatchSize"/> messages per room.
    /// </summary>
    /// <returns>Number of messages deleted for this room.</returns>
    private async Task<int> DeleteExpiredMessagesForRoomAsync(
        Guid roomId,
        DateTimeOffset cutoff,
        IJsonQueryableStateStore<ChatMessageModel> messageStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "MessageRetentionWorker.DeleteExpiredMessagesForRoom");

        // Query messages older than the retention cutoff for this room
        // Messages are keyed as "{roomId}:{messageId}" -- filter by RoomId and Timestamp
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.RoomId", Operator = QueryOperator.Equals, Value = roomId },
            new() { Path = "$.Timestamp", Operator = QueryOperator.LessThan, Value = cutoff.ToString("O") },
        };

        var result = await messageStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MessageRetentionBatchSize,
            new JsonSortSpec { Path = "$.Timestamp", Descending = false },
            cancellationToken);

        var deletedCount = 0;
        foreach (var item in result.Items)
        {
            try
            {
                await messageStore.DeleteAsync(item.Key, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired message {MessageKey}", item.Key);
            }
        }

        return deletedCount;
    }
}
