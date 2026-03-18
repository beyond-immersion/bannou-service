using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Arbitration;

/// <summary>
/// Partial class for ArbitrationService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ArbitrationService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IArbitrationService, ContractMilestoneCompletedEvent>(
            "contract.milestone.completed",
            async (svc, evt) => await ((ArbitrationService)svc).HandleMilestoneCompletedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, ContractMilestoneFailedEvent>(
            "contract.milestone.failed",
            async (svc, evt) => await ((ArbitrationService)svc).HandleMilestoneFailedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((ArbitrationService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((ArbitrationService)svc).HandleContractTerminatedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionTerritoryClaimedEvent>(
            "faction.territory.claimed",
            async (svc, evt) => await ((ArbitrationService)svc).HandleTerritoryClaimedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionTerritoryReleasedEvent>(
            "faction.territory.released",
            async (svc, evt) => await ((ArbitrationService)svc).HandleTerritoryReleasedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionGovernanceDefinedEvent>(
            "faction.governance.defined",
            async (svc, evt) => await ((ArbitrationService)svc).HandleGovernanceDefinedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionGovernanceDeletedEvent>(
            "faction.governance.deleted",
            async (svc, evt) => await ((ArbitrationService)svc).HandleGovernanceDeletedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionAuthorityDelegatedEvent>(
            "faction.authority.delegated",
            async (svc, evt) => await ((ArbitrationService)svc).HandleAuthorityDelegatedAsync(evt));

        eventConsumer.RegisterHandler<IArbitrationService, FactionAuthorityRevokedEvent>(
            "faction.authority.revoked",
            async (svc, evt) => await ((ArbitrationService)svc).HandleAuthorityRevokedAsync(evt));

    }

    /// <summary>
    /// Handles contract.milestone.completed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleMilestoneCompletedAsync(ContractMilestoneCompletedEvent evt)
    {
        // TODO: Implement contract.milestone.completed event handling
        _logger.LogInformation("Received {Topic} event", "contract.milestone.completed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.milestone.failed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleMilestoneFailedAsync(ContractMilestoneFailedEvent evt)
    {
        // TODO: Implement contract.milestone.failed event handling
        _logger.LogInformation("Received {Topic} event", "contract.milestone.failed");
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
    /// Handles faction.territory.claimed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleTerritoryClaimedAsync(FactionTerritoryClaimedEvent evt)
    {
        // TODO: Implement faction.territory.claimed event handling
        _logger.LogInformation("Received {Topic} event", "faction.territory.claimed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles faction.territory.released events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleTerritoryReleasedAsync(FactionTerritoryReleasedEvent evt)
    {
        // TODO: Implement faction.territory.released event handling
        _logger.LogInformation("Received {Topic} event", "faction.territory.released");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles faction.governance.defined events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleGovernanceDefinedAsync(FactionGovernanceDefinedEvent evt)
    {
        // TODO: Implement faction.governance.defined event handling
        _logger.LogInformation("Received {Topic} event", "faction.governance.defined");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles faction.governance.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleGovernanceDeletedAsync(FactionGovernanceDeletedEvent evt)
    {
        // TODO: Implement faction.governance.deleted event handling
        _logger.LogInformation("Received {Topic} event", "faction.governance.deleted");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles faction.authority.delegated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleAuthorityDelegatedAsync(FactionAuthorityDelegatedEvent evt)
    {
        // TODO: Implement faction.authority.delegated event handling
        _logger.LogInformation("Received {Topic} event", "faction.authority.delegated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles faction.authority.revoked events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleAuthorityRevokedAsync(FactionAuthorityRevokedEvent evt)
    {
        // TODO: Implement faction.authority.revoked event handling
        _logger.LogInformation("Received {Topic} event", "faction.authority.revoked");
        return Task.CompletedTask;
    }
}
