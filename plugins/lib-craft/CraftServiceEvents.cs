using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Craft;

/// <summary>
/// Partial class for CraftService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class CraftService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ICraftService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((CraftService)svc).HandleContractTerminatedAsync(evt));

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
}
