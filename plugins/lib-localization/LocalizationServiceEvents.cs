using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Partial class for LocalizationService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class LocalizationService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
    }
}
