using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Partial class for ChatService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ChatService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IChatService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((ChatService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractBreachDetectedEvent>(
            "contract.breach.detected",
            async (svc, evt) => await ((ChatService)svc).HandleContractBreachDetectedAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((ChatService)svc).HandleContractTerminatedAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractExpiredEvent>(
            "contract.expired",
            async (svc, evt) => await ((ChatService)svc).HandleContractExpiredAsync(evt));

    }

    /// <summary>
    /// Handles contract fulfillment by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract fulfilled event data.</param>
    public async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        // TODO: Implement - look up rooms by contractId, apply configured action
        _logger.LogInformation("Received contract fulfilled event for contract {ContractId}", evt.ContractId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract breach by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract breach detected event data.</param>
    public async Task HandleContractBreachDetectedAsync(ContractBreachDetectedEvent evt)
    {
        // TODO: Implement - look up rooms by contractId, apply configured action
        _logger.LogInformation("Received contract breach detected event for contract {ContractId}", evt.ContractId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract termination by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract terminated event data.</param>
    public async Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        // TODO: Implement - look up rooms by contractId, apply configured action
        _logger.LogInformation("Received contract terminated event for contract {ContractId}", evt.ContractId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract expiration by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract expired event data.</param>
    public async Task HandleContractExpiredAsync(ContractExpiredEvent evt)
    {
        // TODO: Implement - look up rooms by contractId, apply configured action
        _logger.LogInformation("Received contract expired event for contract {ContractId}", evt.ContractId);
        await Task.CompletedTask;
    }
}
