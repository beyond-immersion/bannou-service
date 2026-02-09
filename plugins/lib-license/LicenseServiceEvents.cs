using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
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
    /// Handles character.deleted events by cleaning up all boards for the deleted character.
    /// Deletes inventory containers (destroying contained license items) and board instance records.
    /// </summary>
    /// <param name="evt">The character deleted event data.</param>
    public async Task HandleCharacterDeletedAsync(CharacterDeletedEvent evt)
    {
        _logger.LogInformation("Handling character deleted event for character {CharacterId}", evt.CharacterId);

        try
        {
            var boards = await BoardStore.QueryAsync(
                b => b.CharacterId == evt.CharacterId,
                cancellationToken: CancellationToken.None);

            if (boards.Count == 0)
            {
                _logger.LogDebug("No license boards found for character {CharacterId}", evt.CharacterId);
                return;
            }

            _logger.LogInformation(
                "Cleaning up {BoardCount} license boards for character {CharacterId}",
                boards.Count, evt.CharacterId);

            foreach (var board in boards)
            {
                try
                {
                    // Delete the inventory container (destroys all contained license items)
                    await _inventoryClient.DeleteContainerAsync(
                        new DeleteContainerRequest { ContainerId = board.ContainerId },
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to delete container {ContainerId} for board {BoardId} during character cleanup",
                        board.ContainerId, board.BoardId);
                }

                // Delete the board instance record
                await BoardStore.DeleteAsync(
                    BuildBoardKey(board.BoardId),
                    CancellationToken.None);

                // Delete the character-template uniqueness key
                await BoardStore.DeleteAsync(
                    BuildBoardByCharacterKey(board.CharacterId, board.BoardTemplateId),
                    CancellationToken.None);

                // Invalidate board cache
                await BoardCache.DeleteAsync(
                    BuildBoardCacheKey(board.BoardId),
                    CancellationToken.None);

                _logger.LogDebug("Deleted board {BoardId} for character {CharacterId}", board.BoardId, evt.CharacterId);
            }

            _logger.LogInformation(
                "Completed cleanup of {BoardCount} license boards for character {CharacterId}",
                boards.Count, evt.CharacterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up license boards for character {CharacterId}", evt.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "license",
                "HandleCharacterDeleted",
                "cleanup_failed",
                ex.Message,
                dependency: null,
                endpoint: "event:character.deleted",
                details: $"CharacterId: {evt.CharacterId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }
}
