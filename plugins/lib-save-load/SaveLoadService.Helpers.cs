using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.SaveLoad.Models;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad;

// =============================================================================
// SaveLoadService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by SaveLoadService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (SaveLoadService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ISaveLoadService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (SaveLoadService.Helpers.cs):
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
/// Private and internal helper methods for SaveLoadService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class SaveLoadService
{
    /// <summary>
    /// Finds a slot by owner and name (without gameId).
    /// Used by version operations that don't include gameId in request.
    /// </summary>
    private async Task<SaveSlotMetadata?> FindSlotByOwnerAndNameAsync(
        Guid ownerId,
        EntityType ownerType,
        string slotName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveLoadService.FindSlotByOwnerAndNameAsync");
        var matchingSlots = await _slotQueryStore.QueryAsync(
            s => s.OwnerId == ownerId &&
                s.OwnerType == ownerType &&
                s.SlotName == slotName,
            cancellationToken);

        return matchingSlots.FirstOrDefault();
    }

    private async Task PublishSaveSlotCreatedEventAsync(SaveSlotMetadata slot, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveLoadService.PublishSaveSlotCreatedEventAsync");
        var eventModel = new SaveSlotCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt
        };

        await _messageBus.PublishSaveSlotCreatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published SaveSlotCreatedEvent for slot: {SlotId}", slot.SlotId);
    }

    private async Task PublishSaveSlotUpdatedEventAsync(SaveSlotMetadata slot, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveLoadService.PublishSaveSlotUpdatedEventAsync");
        var eventModel = new SaveSlotUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishSaveSlotUpdatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published SaveSlotUpdatedEvent for slot: {SlotId}, changed: {ChangedFields}", slot.SlotId, string.Join(", ", changedFields));
    }

    private async Task PublishSaveSlotDeletedEventAsync(SaveSlotMetadata slot, string? deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveLoadService.PublishSaveSlotDeletedEventAsync");
        var eventModel = new SaveSlotDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.PublishSaveSlotDeletedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published SaveSlotDeletedEvent for slot: {SlotId}", slot.SlotId);
    }
}
