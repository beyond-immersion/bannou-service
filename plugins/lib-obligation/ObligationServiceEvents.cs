using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Partial class for ObligationService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// Subscribes to contract lifecycle events to keep the obligation cache in sync.
/// When a contract is activated, the cache is rebuilt to include its behavioral clauses.
/// When a contract ends (terminated, fulfilled, expired), the cache is rebuilt to exclude it.
/// </remarks>
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
    /// Handles contract.activated events by rebuilding obligation caches for all character parties.
    /// A newly activated contract may contain behavioral clauses that create obligations.
    /// </summary>
    /// <param name="evt">The contract activated event.</param>
    public async Task HandleContractActivatedAsync(ContractActivatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.HandleContractActivatedAsync");
        _logger.LogDebug("Handling contract.activated for contract {ContractId}", evt.ContractId);

        var characterIds = ExtractCharacterPartyIds(evt.Parties);
        if (characterIds.Count == 0)
        {
            _logger.LogDebug("No character parties in contract {ContractId}, skipping", evt.ContractId);
            return;
        }

        foreach (var characterId in characterIds)
        {
            try
            {
                await RebuildObligationCacheAsync(characterId, "contract.activated", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to rebuild obligation cache for character {CharacterId} on contract activation {ContractId}",
                    characterId, evt.ContractId);
                await _messageBus.TryPublishErrorAsync(
                    "obligation", "cache_rebuild",
                    "cache_rebuild_failed",
                    $"Failed to rebuild cache for character {characterId} on contract.activated: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles contract.terminated events by rebuilding obligation caches.
    /// Queries the contract to discover affected character parties since the event lacks party data.
    /// </summary>
    /// <param name="evt">The contract terminated event.</param>
    public async Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.HandleContractTerminatedAsync");
        _logger.LogDebug("Handling contract.terminated for contract {ContractId}", evt.ContractId);

        var characterIds = await GetCharacterPartiesFromContractAsync(evt.ContractId);
        if (characterIds.Count == 0) return;

        foreach (var characterId in characterIds)
        {
            try
            {
                await RebuildObligationCacheAsync(characterId, "contract.terminated", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to rebuild obligation cache for character {CharacterId} on contract termination {ContractId}",
                    characterId, evt.ContractId);
                await _messageBus.TryPublishErrorAsync(
                    "obligation", "cache_rebuild",
                    "cache_rebuild_failed",
                    $"Failed to rebuild cache for character {characterId} on contract.terminated: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles contract.fulfilled events by rebuilding obligation caches for all character parties.
    /// A fulfilled contract's behavioral clauses are no longer active.
    /// </summary>
    /// <param name="evt">The contract fulfilled event.</param>
    public async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.HandleContractFulfilledAsync");
        _logger.LogDebug("Handling contract.fulfilled for contract {ContractId}", evt.ContractId);

        var characterIds = ExtractCharacterPartyIds(evt.Parties);
        if (characterIds.Count == 0)
        {
            _logger.LogDebug("No character parties in contract {ContractId}, skipping", evt.ContractId);
            return;
        }

        foreach (var characterId in characterIds)
        {
            try
            {
                await RebuildObligationCacheAsync(characterId, "contract.fulfilled", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to rebuild obligation cache for character {CharacterId} on contract fulfillment {ContractId}",
                    characterId, evt.ContractId);
                await _messageBus.TryPublishErrorAsync(
                    "obligation", "cache_rebuild",
                    "cache_rebuild_failed",
                    $"Failed to rebuild cache for character {characterId} on contract.fulfilled: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles contract.expired events by rebuilding obligation caches.
    /// Queries the contract to discover affected character parties since the event lacks party data.
    /// </summary>
    /// <param name="evt">The contract expired event.</param>
    public async Task HandleContractExpiredAsync(ContractExpiredEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.HandleContractExpiredAsync");
        _logger.LogDebug("Handling contract.expired for contract {ContractId}", evt.ContractId);

        var characterIds = await GetCharacterPartiesFromContractAsync(evt.ContractId);
        if (characterIds.Count == 0) return;

        foreach (var characterId in characterIds)
        {
            try
            {
                await RebuildObligationCacheAsync(characterId, "contract.expired", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to rebuild obligation cache for character {CharacterId} on contract expiry {ContractId}",
                    characterId, evt.ContractId);
                await _messageBus.TryPublishErrorAsync(
                    "obligation", "cache_rebuild",
                    "cache_rebuild_failed",
                    $"Failed to rebuild cache for character {characterId} on contract.expired: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // Internal: Event Helper Methods
    // ========================================================================

    /// <summary>
    /// Extracts character entity IDs from a collection of contract party info.
    /// </summary>
    private static List<Guid> ExtractCharacterPartyIds(IEnumerable<PartyInfo> parties)
    {
        return parties
            .Where(p => p.EntityType == EntityType.Character)
            .Select(p => p.EntityId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Queries the contract service to get character party IDs for a contract.
    /// Used for events (terminated, expired) that do not include party information.
    /// </summary>
    private async Task<List<Guid>> GetCharacterPartiesFromContractAsync(Guid contractId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.GetCharacterPartiesFromContractAsync");
        try
        {
            var contractInstance = await _contractClient.GetContractInstanceAsync(
                new GetContractInstanceRequest { ContractId = contractId },
                CancellationToken.None);

            return contractInstance.Parties
                .Where(p => p.EntityType == EntityType.Character)
                .Select(p => p.EntityId)
                .Distinct()
                .ToList();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to query contract {ContractId} for party information, status {Status}",
                contractId, ex.StatusCode);
            return new List<Guid>();
        }
    }
}
