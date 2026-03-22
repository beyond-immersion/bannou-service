using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.License;

// =============================================================================
// LicenseService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by LicenseService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (LicenseService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ILicenseService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (LicenseService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for LicenseService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class LicenseService
{
    /// <summary>
    /// Publishes a license.unlock-failed event.
    /// </summary>
    private async Task PublishUnlockFailedAsync(
        Guid boardId, EntityType ownerType, Guid ownerId, string licenseCode,
        UnlockFailureReason reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.license", "LicenseService.PublishUnlockFailedAsync");
        await _messageBus.PublishLicenseUnlockFailedAsync(
            new LicenseUnlockFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                BoardId = boardId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                LicenseCode = licenseCode,
                Reason = reason
            },
            cancellationToken);
    }

    /// <summary>
    /// Compensates for a failed contract by destroying the item created during the unlock flow.
    /// Called when contract execution fails after item creation succeeded (saga compensation).
    /// </summary>
    private async Task CompensateItemCreationAsync(
        Guid itemInstanceId, Guid boardId, string licenseCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.license", "LicenseService.CompensateItemCreationAsync");
        try
        {
            await _itemClient.DestroyItemInstanceAsync(
                new DestroyItemInstanceRequest { InstanceId = itemInstanceId },
                cancellationToken);
            _logger.LogInformation(
                "Compensation successful: destroyed item {ItemInstanceId} after contract failure for {Code} on board {BoardId}",
                itemInstanceId, licenseCode, boardId);
        }
        catch (Exception compensationEx)
        {
            // Compensation failure is serious but must not mask the original error.
            // The orphaned item will be cleaned up when the board is deleted or manually reconciled.
            _logger.LogError(compensationEx,
                "Compensation FAILED: could not destroy item {ItemInstanceId} after contract failure for {Code} on board {BoardId}. Orphaned item requires manual cleanup",
                itemInstanceId, licenseCode, boardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "CompensateItemCreation", "compensation_failed",
                $"Failed to destroy orphaned item {itemInstanceId} after contract failure",
                dependency: "item", endpoint: "post:/item/instance/destroy",
                details: null, stack: compensationEx.StackTrace, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Loads or rebuilds the board cache. Tries Redis cache first; on miss,
    /// queries inventory as the authoritative source and rebuilds the cache.
    /// </summary>
    private async Task<BoardCacheModel> LoadOrRebuildBoardCacheAsync(
        BoardInstanceModel board,
        IReadOnlyList<LicenseDefinitionModel> definitions,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.license", "LicenseService.LoadOrRebuildBoardCacheAsync");
        // Try cache first
        var cache = await _boardCache.GetAsync(BuildBoardCacheKey(board.BoardId), cancellationToken);
        if (cache != null)
        {
            return cache;
        }

        // Cache miss: rebuild from inventory (authoritative source)
        _logger.LogInformation("Board cache miss for board {BoardId}, rebuilding from inventory", board.BoardId);

        var containerContents = await _inventoryClient.GetContainerAsync(
            new GetContainerRequest { ContainerId = board.ContainerId, IncludeContents = true },
            cancellationToken);

        // Build a lookup from item template ID to definition for matching
        var defsByTemplateId = definitions
            .GroupBy(d => d.ItemTemplateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var unlockedEntries = new List<UnlockedLicenseEntry>();
        foreach (var item in containerContents.Items)
        {
            if (defsByTemplateId.TryGetValue(item.TemplateId, out var matchingDefs))
            {
                // Find the definition that matches this item (could be multiple defs with same template)
                // Use the first unmatched one
                var matchedDef = matchingDefs.FirstOrDefault(d =>
                    !unlockedEntries.Any(u => u.Code == d.Code));

                if (matchedDef != null)
                {
                    unlockedEntries.Add(new UnlockedLicenseEntry
                    {
                        Code = matchedDef.Code,
                        PositionX = matchedDef.PositionX,
                        PositionY = matchedDef.PositionY,
                        ItemInstanceId = item.InstanceId,
                        UnlockedAt = board.CreatedAt // Best approximation when rebuilding
                    });
                }
            }
        }

        cache = new BoardCacheModel
        {
            BoardId = board.BoardId,
            UnlockedPositions = unlockedEntries,
            LastUpdated = DateTimeOffset.UtcNow
        };

        // Persist rebuilt cache
        await _boardCache.SaveAsync(
            BuildBoardCacheKey(board.BoardId),
            cache,
            new StateOptions { Ttl = _configuration.BoardCacheTtlSeconds },
            cancellationToken);

        return cache;
    }
}
