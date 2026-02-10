using BeyondImmersion.BannouService.Events;

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
    /// Handles contract.fulfilled events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        // TODO: Implement contract.fulfilled event handling
        _logger.LogInformation("[EVENT] Received contract.fulfilled event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.breach.detected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractBreachDetectedAsync(ContractBreachDetectedEvent evt)
    {
        // TODO: Implement contract.breach.detected event handling
        _logger.LogInformation("[EVENT] Received contract.breach.detected event");
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
        _logger.LogInformation("[EVENT] Received contract.terminated event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.expired events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractExpiredAsync(ContractExpiredEvent evt)
    {
        // TODO: Implement contract.expired event handling
        _logger.LogInformation("[EVENT] Received contract.expired event");
        return Task.CompletedTask;
    }
}
