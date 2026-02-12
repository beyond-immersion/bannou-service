using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Partial class for ObligationService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ObligationService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IObligationService, ContractActivatedEvent>(
            "contract.activated",
            async (svc, evt) => await ((ObligationService)svc).HandleContractActivatedAsync(evt));

        eventConsumer.RegisterHandler<IObligationService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((ObligationService)svc).HandleContractTerminatedAsync(evt));

        eventConsumer.RegisterHandler<IObligationService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((ObligationService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IObligationService, ContractExpiredEvent>(
            "contract.expired",
            async (svc, evt) => await ((ObligationService)svc).HandleContractExpiredAsync(evt));

    }

    /// <summary>
    /// Handles contract.activated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractActivatedAsync(ContractActivatedEvent evt)
    {
        // TODO: Implement contract.activated event handling
        _logger.LogInformation("Received {Topic} event", "contract.activated");
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
        _logger.LogInformation("Received {Topic} event", "contract.terminated");
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
        _logger.LogInformation("Received {Topic} event", "contract.fulfilled");
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
        _logger.LogInformation("Received {Topic} event", "contract.expired");
        return Task.CompletedTask;
    }
}
