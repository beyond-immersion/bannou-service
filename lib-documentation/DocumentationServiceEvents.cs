using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// Partial class for DocumentationService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// Per the events schema (x-event-subscriptions: []), this service has minimal
/// event subscriptions - sessions use TTL cleanup, no external events needed.
/// </remarks>
public partial class DocumentationService
{
    /// <summary>
    /// Registers event consumers for Dapr pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    /// <remarks>
    /// Documentation service has minimal event subscriptions per schema design.
    /// Sessions use TTL-based cleanup rather than event-driven invalidation.
    /// </remarks>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Minimal event subscriptions per schema (x-event-subscriptions: [])
        // No external event consumption needed - sessions use TTL cleanup
        _logger.LogDebug("Documentation service event consumers registered (minimal mode)");
    }
}
