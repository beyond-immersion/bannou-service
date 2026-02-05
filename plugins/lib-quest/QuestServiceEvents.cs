using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Partial class for QuestService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
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

    }

    /// <summary>
    /// Handles contract.milestone.completed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractMilestoneCompletedAsync(ContractMilestoneCompletedEvent evt)
    {
        // TODO: Implement contract.milestone.completed event handling
        _logger.LogInformation("Received contract.milestone.completed event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.fulfilled events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        // TODO: Implement contract.fulfilled event handling
        _logger.LogInformation("Received contract.fulfilled event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.terminated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        // TODO: Implement contract.terminated event handling
        _logger.LogInformation("Received contract.terminated event");
        return Task.CompletedTask;
    }
}
