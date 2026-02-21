using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Partial class for QuestService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// Per IMPLEMENTATION TENETS: All event handlers must be async with proper await.
/// </remarks>
public partial class QuestService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IQuestService, ContractMilestoneCompletedEvent>(
            "contract.milestone.completed",
            async (svc, evt) => await ((QuestService)svc).HandleContractMilestoneCompletedAsync(evt));

        eventConsumer.RegisterHandler<IQuestService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((QuestService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IQuestService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((QuestService)svc).HandleContractTerminatedAsync(evt));

        // Self-subscribe to our own events for cache invalidation.
        // When quest state changes, invalidate the cache so running actors get fresh data.
        eventConsumer.RegisterHandler<IQuestService, QuestAcceptedEvent>(
            QuestTopics.QuestAccepted,
            async (svc, evt) => await ((QuestService)svc).HandleQuestAcceptedForCacheAsync(evt));

        eventConsumer.RegisterHandler<IQuestService, QuestCompletedEvent>(
            QuestTopics.QuestCompleted,
            async (svc, evt) => await ((QuestService)svc).HandleQuestCompletedForCacheAsync(evt));

        eventConsumer.RegisterHandler<IQuestService, QuestFailedEvent>(
            QuestTopics.QuestFailed,
            async (svc, evt) => await ((QuestService)svc).HandleQuestFailedForCacheAsync(evt));

        eventConsumer.RegisterHandler<IQuestService, QuestAbandonedEvent>(
            QuestTopics.QuestAbandoned,
            async (svc, evt) => await ((QuestService)svc).HandleQuestAbandonedForCacheAsync(evt));
    }

    /// <summary>
    /// Handles contract.milestone.completed events.
    /// Updates quest objective state when Contract service confirms milestone completion.
    /// </summary>
    /// <param name="evt">The event data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleContractMilestoneCompletedAsync(ContractMilestoneCompletedEvent evt)
    {
        _logger.LogDebug(
            "Handling contract.milestone.completed: ContractId={ContractId}, MilestoneCode={MilestoneCode}",
            evt.ContractId,
            evt.MilestoneCode);

        try
        {
            // Find quest instance by contract ID
            var instances = await InstanceStore.QueryAsync(
                i => i.ContractInstanceId == evt.ContractId,
                cancellationToken: CancellationToken.None);

            var instance = instances.FirstOrDefault();
            if (instance == null)
            {
                // Contract may not be quest-related - not an error
                _logger.LogDebug("No quest instance found for contract {ContractId}", evt.ContractId);
                return;
            }

            // The objective progress should already be updated via ReportObjectiveProgress.
            // This event handler ensures consistency if the contract milestone was completed
            // through other means (e.g., direct Contract API calls, prebound APIs).
            var progressKey = BuildProgressKey(instance.QuestInstanceId, evt.MilestoneCode);
            var progress = await ProgressStore.GetAsync(progressKey, CancellationToken.None);

            if (progress != null && !progress.IsComplete)
            {
                // Mark objective as complete if contract says the milestone is done
                progress.CurrentCount = progress.RequiredCount;
                progress.IsComplete = true;

                await ProgressStore.SaveAsync(
                    progressKey,
                    progress,
                    new StateOptions { Ttl = _configuration.ProgressCacheTtlSeconds },
                    CancellationToken.None);

                _logger.LogInformation(
                    "Objective {ObjectiveCode} marked complete via contract milestone event for quest {QuestInstanceId}",
                    evt.MilestoneCode,
                    instance.QuestInstanceId);

                // Publish progress event
                var progressEvent = new QuestObjectiveProgressedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    QuestInstanceId = instance.QuestInstanceId,
                    QuestCode = instance.Code,
                    ObjectiveCode = evt.MilestoneCode,
                    CurrentCount = progress.CurrentCount,
                    RequiredCount = progress.RequiredCount,
                    IsComplete = true
                };
                await _messageBus.TryPublishAsync(
                    QuestTopics.QuestObjectiveProgressed,
                    progressEvent,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling contract.milestone.completed for contract {ContractId}",
                evt.ContractId);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "HandleContractMilestoneCompleted",
                "event_handler_exception",
                ex.Message,
                dependency: "contract",
                endpoint: "event:contract.milestone.completed",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles contract.fulfilled events.
    /// Marks quest as completed when all required milestones are done.
    /// </summary>
    /// <param name="evt">The event data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        _logger.LogDebug("Handling contract.fulfilled: ContractId={ContractId}", evt.ContractId);

        try
        {
            // Find quest instance by contract ID
            var instances = await InstanceStore.QueryAsync(
                i => i.ContractInstanceId == evt.ContractId,
                cancellationToken: CancellationToken.None);

            var instance = instances.FirstOrDefault();
            if (instance == null)
            {
                _logger.LogDebug("No quest instance found for contract {ContractId}", evt.ContractId);
                return;
            }

            if (instance.Status != QuestStatus.ACTIVE)
            {
                _logger.LogDebug(
                    "Quest {QuestInstanceId} not in ACTIVE status, ignoring contract fulfilled event",
                    instance.QuestInstanceId);
                return;
            }

            // Use the existing helper to complete the quest
            await CompleteQuestAsync(instance, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract.fulfilled for contract {ContractId}", evt.ContractId);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "HandleContractFulfilled",
                "event_handler_exception",
                ex.Message,
                dependency: "contract",
                endpoint: "event:contract.fulfilled",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles contract.terminated events.
    /// Marks quest as failed or abandoned depending on termination reason.
    /// </summary>
    /// <param name="evt">The event data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        _logger.LogDebug(
            "Handling contract.terminated: ContractId={ContractId}, Reason={Reason}",
            evt.ContractId,
            evt.Reason);

        try
        {
            // Find quest instance by contract ID
            var instances = await InstanceStore.QueryAsync(
                i => i.ContractInstanceId == evt.ContractId,
                cancellationToken: CancellationToken.None);

            var instance = instances.FirstOrDefault();
            if (instance == null)
            {
                _logger.LogDebug("No quest instance found for contract {ContractId}", evt.ContractId);
                return;
            }

            if (instance.Status != QuestStatus.ACTIVE)
            {
                _logger.LogDebug(
                    "Quest {QuestInstanceId} not in ACTIVE status, ignoring contract terminated event",
                    instance.QuestInstanceId);
                return;
            }

            // Determine if this is abandonment or failure based on reason
            var isAbandonment = evt.Reason?.Contains("abandoned", StringComparison.OrdinalIgnoreCase) == true ||
                                evt.Reason?.Contains("player", StringComparison.OrdinalIgnoreCase) == true;

            var newStatus = isAbandonment ? QuestStatus.ABANDONED : QuestStatus.FAILED;
            var now = DateTimeOffset.UtcNow;

            await FailOrAbandonQuestAsync(instance, newStatus, evt.Reason ?? "Contract terminated", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract.terminated for contract {ContractId}", evt.ContractId);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "HandleContractTerminated",
                "event_handler_exception",
                ex.Message,
                dependency: "contract",
                endpoint: "event:contract.terminated",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Marks a quest as failed or abandoned and updates all related state.
    /// </summary>
    private async Task FailOrAbandonQuestAsync(
        QuestInstanceModel instance,
        QuestStatus newStatus,
        string reason,
        DateTimeOffset timestamp)
    {
        var instanceKey = BuildInstanceKey(instance.QuestInstanceId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (current, etag) = await InstanceStore.GetWithETagAsync(instanceKey, CancellationToken.None);
            if (current == null || current.Status != QuestStatus.ACTIVE)
            {
                return;
            }

            current.Status = newStatus;
            current.CompletedAt = timestamp;

            var saveResult = await InstanceStore.TrySaveAsync(
                instanceKey,
                current,
                etag ?? string.Empty,
                cancellationToken: CancellationToken.None);

            if (saveResult == null)
            {
                _logger.LogDebug(
                    "Concurrent modification updating quest {QuestInstanceId} status, retrying (attempt {Attempt})",
                    instance.QuestInstanceId,
                    attempt + 1);
                continue;
            }

            // Update character indexes - remove from active quests
            foreach (var characterId in current.QuestorCharacterIds)
            {
                var characterIndexKey = BuildCharacterIndexKey(characterId);
                var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, CancellationToken.None);
                if (characterIndex?.ActiveQuestIds != null)
                {
                    characterIndex.ActiveQuestIds.Remove(instance.QuestInstanceId);
                    await CharacterIndex.SaveAsync(
                        characterIndexKey,
                        characterIndex,
                        cancellationToken: CancellationToken.None);
                }
            }

            // Publish appropriate event
            if (newStatus == QuestStatus.ABANDONED)
            {
                var abandonedEvent = new QuestAbandonedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = timestamp,
                    QuestInstanceId = instance.QuestInstanceId,
                    QuestCode = current.Code,
                    // Use first questor as the abandoning character for event-driven abandonment
                    AbandoningCharacterId = current.QuestorCharacterIds.FirstOrDefault()
                };
                await _messageBus.TryPublishAsync(
                    QuestTopics.QuestAbandoned,
                    abandonedEvent,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation(
                    "Quest abandoned via contract termination: {QuestInstanceId} ({Code})",
                    instance.QuestInstanceId,
                    current.Code);
            }
            else
            {
                var failedEvent = new QuestFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = timestamp,
                    QuestInstanceId = instance.QuestInstanceId,
                    QuestCode = current.Code,
                    QuestorCharacterIds = current.QuestorCharacterIds,
                    Reason = reason
                };
                await _messageBus.TryPublishAsync(
                    QuestTopics.QuestFailed,
                    failedEvent,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation(
                    "Quest failed: {QuestInstanceId} ({Code}), reason: {Reason}",
                    instance.QuestInstanceId,
                    current.Code,
                    reason);
            }

            return;
        }

        _logger.LogWarning(
            "Failed to update quest {QuestInstanceId} status to {Status} after {MaxRetries} attempts",
            instance.QuestInstanceId,
            newStatus,
            _configuration.MaxConcurrencyRetries);
    }

    #region Cache Invalidation Handlers

    /// <summary>
    /// Handles quest.accepted events for cache invalidation.
    /// </summary>
    private async Task HandleQuestAcceptedForCacheAsync(QuestAcceptedEvent evt)
    {
        _logger.LogDebug("Invalidating quest cache for accepted quest {QuestCode}", evt.QuestCode);
        foreach (var characterId in evt.QuestorCharacterIds)
        {
            _questDataCache.Invalidate(characterId);
        }
        await Task.Yield();
    }

    /// <summary>
    /// Handles quest.completed events for cache invalidation.
    /// </summary>
    private async Task HandleQuestCompletedForCacheAsync(QuestCompletedEvent evt)
    {
        _logger.LogDebug("Invalidating quest cache for completed quest {QuestCode}", evt.QuestCode);
        foreach (var characterId in evt.QuestorCharacterIds)
        {
            _questDataCache.Invalidate(characterId);
        }
        await Task.Yield();
    }

    /// <summary>
    /// Handles quest.failed events for cache invalidation.
    /// </summary>
    private async Task HandleQuestFailedForCacheAsync(QuestFailedEvent evt)
    {
        _logger.LogDebug("Invalidating quest cache for failed quest {QuestCode}", evt.QuestCode);
        if (evt.QuestorCharacterIds != null)
        {
            foreach (var characterId in evt.QuestorCharacterIds)
            {
                _questDataCache.Invalidate(characterId);
            }
        }
        await Task.Yield();
    }

    /// <summary>
    /// Handles quest.abandoned events for cache invalidation.
    /// </summary>
    private async Task HandleQuestAbandonedForCacheAsync(QuestAbandonedEvent evt)
    {
        _logger.LogDebug("Invalidating quest cache for abandoned quest {QuestCode}", evt.QuestCode);
        _questDataCache.Invalidate(evt.AbandoningCharacterId);
        await Task.Yield();
    }

    #endregion
}
