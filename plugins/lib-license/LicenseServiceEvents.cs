using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.License;

/// <summary>
/// Partial class for LicenseService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class LicenseService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ILicenseService, CharacterDeletedEvent>(
            "character.deleted",
            async (svc, evt) => await ((LicenseService)svc).HandleCharacterDeletedAsync(evt));

    }

    /// <summary>
    /// Handles character.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleCharacterDeletedAsync(CharacterDeletedEvent evt)
    {
        // TODO: Implement character.deleted event handling
        _logger.LogInformation("Received character.deleted event");
        return Task.CompletedTask;
    }
}
