using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Partial class for PuppetmasterService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class PuppetmasterService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IPuppetmasterService, RealmCreatedEvent>(
            "realm.created",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleRealmCreatedAsync(evt));
    }

    /// <summary>
    /// Handles realm.created events.
    /// Auto-starts regional watchers for newly created realms.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmCreatedAsync(RealmCreatedEvent evt)
    {
        _logger.LogInformation(
            "Received realm.created event for realm {RealmId} ({RealmCode})",
            evt.RealmId,
            evt.Code);

        // Only start watchers for active realms
        if (!evt.IsActive)
        {
            _logger.LogDebug(
                "Realm {RealmId} is not active, skipping watcher creation",
                evt.RealmId);
            return;
        }

        // Auto-start regional watchers for the new realm
        var (status, response) = await StartWatchersForRealmAsync(
            new StartWatchersForRealmRequest { RealmId = evt.RealmId },
            CancellationToken.None);

        if (status == StatusCodes.OK && response != null)
        {
            _logger.LogInformation(
                "Auto-started {Count} watchers for new realm {RealmId}",
                response.WatchersStarted,
                evt.RealmId);
        }
        else
        {
            _logger.LogWarning(
                "Failed to auto-start watchers for new realm {RealmId}: status {Status}",
                evt.RealmId,
                status);
        }
    }
}
