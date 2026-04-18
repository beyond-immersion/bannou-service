using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Realm;

// =============================================================================
// RealmService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by RealmService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (RealmService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IRealmService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (RealmService.Helpers.cs):
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
/// Private and internal helper methods for RealmService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class RealmService
{
    // Move private/internal helper methods here from RealmService.cs
    #region Helper Methods

    /// <summary>
    /// Add a realm ID to the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task AddToRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.AddToRealmListAsync");
        for (var attempt = 0; attempt < _configuration.OptimisticRetryAttempts; attempt++)
        {
            var (realmIds, etag) = await _realmListStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            realmIds ??= new List<Guid>();

            if (realmIds.Contains(realmId))
            {
                return; // Already in list
            }

            realmIds.Add(realmId);
            // etag is null when list key doesn't exist yet; empty string signals
            // "create new" to TrySaveAsync (will never conflict on new entries)
            var result = await _realmListStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add realm {RealmId} to list after {Attempts} attempts", realmId, _configuration.OptimisticRetryAttempts);
    }

    /// <summary>
    /// Remove a realm ID from the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task RemoveFromRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.RemoveFromRealmListAsync");
        for (var attempt = 0; attempt < _configuration.OptimisticRetryAttempts; attempt++)
        {
            var (realmIds, etag) = await _realmListStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            if (realmIds == null || !realmIds.Remove(realmId))
            {
                return; // Not in list or already removed
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await _realmListStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove realm {RealmId} from list after {Attempts} attempts", realmId, _configuration.OptimisticRetryAttempts);
    }

    private async Task<List<RealmModel>> LoadRealmsByIdsAsync(List<Guid> realmIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.LoadRealmsByIdsAsync");
        if (realmIds.Count == 0)
        {
            return new List<RealmModel>();
        }

        var keys = realmIds.Select(BuildRealmKey).ToList();
        var bulkResults = await _realmStore.GetBulkAsync(keys, cancellationToken);

        var realmList = new List<RealmModel>();
        foreach (var (_, model) in bulkResults)
        {
            if (model != null)
            {
                realmList.Add(model);
            }
        }

        return realmList;
    }

    private static RealmResponse MapToResponse(RealmModel model)
    {
        return new RealmResponse
        {
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Attempts to initialize a worldstate clock for a newly created realm.
    /// Logs a warning on failure but does not fail the realm creation -- the clock
    /// can be initialized manually later via the worldstate API.
    /// </summary>
    private async Task TryInitializeWorldstateClockAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.TryInitializeWorldstateClock");

        try
        {
            await _worldstateClient.InitializeRealmClockAsync(
                new InitializeRealmClockRequest
                {
                    RealmId = realmId,
                    CalendarTemplateCode = _configuration.DefaultCalendarTemplateCode
                },
                cancellationToken);

            _logger.LogInformation(
                "Auto-initialized worldstate clock for realm {RealmId} with calendar template {CalendarTemplateCode}",
                realmId, _configuration.DefaultCalendarTemplateCode);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to auto-initialize worldstate clock for realm {RealmId} (status {StatusCode}). Clock can be initialized manually via the worldstate API",
                realmId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "realm",
                "TryInitializeWorldstateClock",
                "WorldstateClockInitFailure",
                ex.Message,
                dependency: "worldstate",
                endpoint: "initialize-realm-clock",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    #endregion
    #region Event Publishing

    /// <summary>
    /// Publishes a realm created event.
    /// </summary>
    private async Task PublishRealmCreatedEventAsync(RealmModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmCreatedEventAsync");
        var eventModel = new RealmCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        await _messageBus.PublishRealmCreatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.created event for {RealmId}", model.RealmId);
    }

    /// <summary>
    /// Publishes a realm updated event with current state and changed fields.
    /// </summary>
    private async Task PublishRealmUpdatedEventAsync(RealmModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmUpdatedEventAsync");
        var eventModel = new RealmUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishRealmUpdatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.updated event for {RealmId} with changed fields: {ChangedFields}",
            model.RealmId, string.Join(", ", changedFields));
    }

    /// <summary>
    /// Publishes a realm merged event with migration statistics.
    /// </summary>
    private async Task PublishRealmMergedEventAsync(
        RealmModel sourceRealm,
        RealmModel targetRealm,
        int totalMigrated,
        int totalFailed,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmMergedEventAsync");
        var eventModel = new RealmMergedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SourceRealmId = sourceRealm.RealmId,
            SourceRealmCode = sourceRealm.Code,
            TargetRealmId = targetRealm.RealmId,
            TargetRealmCode = targetRealm.Code,
            TotalMigrated = totalMigrated,
            TotalFailed = totalFailed
        };

        await _messageBus.PublishRealmMergedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.merged event for source {SourceRealmId} into target {TargetRealmId}",
            sourceRealm.RealmId, targetRealm.RealmId);
    }

    /// <summary>
    /// Publishes a realm deleted event with final state before deletion.
    /// </summary>
    private async Task PublishRealmDeletedEventAsync(RealmModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmDeletedEventAsync");

        var eventModel = new RealmDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.PublishRealmDeletedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.deleted event for {RealmId}", model.RealmId);
    }

    #endregion
}
