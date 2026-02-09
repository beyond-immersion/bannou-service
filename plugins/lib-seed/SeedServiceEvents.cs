using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Partial class for SeedService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class SeedService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ISeedService, SeedGrowthContributedEvent>(
            "seed.growth.contributed",
            async (svc, evt) => await ((SeedService)svc).HandleGrowthContributedAsync(evt));

    }

    /// <summary>
    /// Handles seed.growth.contributed events by recording growth to the specified domain.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGrowthContributedAsync(SeedGrowthContributedEvent evt)
    {
        _logger.LogInformation("Processing growth contribution for seed {SeedId}, domain {Domain}, amount {Amount}",
            evt.SeedId, evt.Domain, evt.Amount);

        try
        {
            var (status, _) = await RecordGrowthInternalAsync(
                evt.SeedId,
                new[] { (evt.Domain, evt.Amount) },
                evt.Source,
                CancellationToken.None);

            if (status != StatusCodes.OK)
            {
                _logger.LogWarning("Growth contribution for seed {SeedId} returned {Status}", evt.SeedId, status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process growth contribution for seed {SeedId}", evt.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "HandleGrowthContributed", "event_processing_failed",
                ex.Message, dependency: null, endpoint: "event:seed.growth.contributed",
                details: null, stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }
}
