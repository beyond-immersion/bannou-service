using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Scene;

/// <summary>
/// Partial class for SceneService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class SceneService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    partial void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
    }
}
