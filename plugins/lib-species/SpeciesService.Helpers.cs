using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Species;

// =============================================================================
// SpeciesService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by SpeciesService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (SpeciesService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ISpeciesService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (SpeciesService.Helpers.cs):
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
/// Private and internal helper methods for SpeciesService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class SpeciesService
{
    #region Realm Validation Helpers

    /// <summary>
    /// Validates that a realm exists and is active (not deprecated).
    /// </summary>
    private async Task<(bool exists, bool isActive)> ValidateRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.ValidateRealmAsync");
        try
        {
            var response = await _realmClient.RealmExistsAsync(
                new RealmExistsRequest { RealmId = realmId },
                cancellationToken);
            return (response.Exists, response.IsActive);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (false, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not validate realm {RealmId} - failing operation (fail closed)", realmId);
            // If RealmService is unavailable, fail the operation - don't assume realm is valid
            throw new InvalidOperationException($"Cannot validate realm {realmId}: RealmService unavailable", ex);
        }
    }

    /// <summary>
    /// Validates multiple realms in parallel and returns lists of invalid and deprecated realm IDs.
    /// </summary>
    private async Task<(List<Guid> invalidRealms, List<Guid> deprecatedRealms)> ValidateRealmsAsync(
        IEnumerable<Guid> realmIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.ValidateRealmsAsync");
        var realmIdList = realmIds.ToList();
        var validationTasks = realmIdList.Select(async realmId =>
        {
            var (exists, isActive) = await ValidateRealmAsync(realmId, cancellationToken);
            return (realmId, exists, isActive);
        });

        var results = await Task.WhenAll(validationTasks);

        var invalidRealms = new List<Guid>();
        var deprecatedRealms = new List<Guid>();

        foreach (var (realmId, exists, isActive) in results)
        {
            if (!exists)
            {
                invalidRealms.Add(realmId);
            }
            else if (!isActive)
            {
                deprecatedRealms.Add(realmId);
            }
        }

        return (invalidRealms, deprecatedRealms);
    }

    #endregion

    private async Task<List<SpeciesModel>> LoadSpeciesByIdsAsync(List<Guid> speciesIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.LoadSpeciesByIdsAsync");
        if (speciesIds.Count == 0)
        {
            return new List<SpeciesModel>();
        }

        var keys = speciesIds.Select(BuildSpeciesKey).ToList();
        var bulkResults = await _speciesStore.GetBulkAsync(keys, cancellationToken);

        var speciesList = new List<SpeciesModel>();
        foreach (var (key, model) in bulkResults)
        {
            if (model != null)
            {
                speciesList.Add(model);
            }
        }

        return speciesList;
    }

    private async Task AddToRealmIndexAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.AddToRealmIndexAsync");
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _idListStore.GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<Guid>();

        if (!speciesIds.Contains(speciesId))
        {
            speciesIds.Add(speciesId);
            await _idListStore.SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.RemoveFromRealmIndexAsync");
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _idListStore.GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<Guid>();

        if (speciesIds.Remove(speciesId))
        {
            await _idListStore.SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    #region Event Publishing

    /// <summary>
    /// Publishes a species created event with full entity state.
    /// </summary>
    private async Task PublishSpeciesCreatedEventAsync(SpeciesModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.PublishSpeciesCreatedEventAsync");
        try
        {
            var eventModel = new SpeciesCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan,
                MaturityAge = model.MaturityAge,
                TraitModifiers = model.TraitModifiers,
                RealmIds = model.RealmIds?.ToList(),
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt
            };

            await _messageBus.PublishSpeciesCreatedAsync(eventModel, cancellationToken);
            _logger.LogDebug("Published species.created event for {SpeciesId}", model.SpeciesId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.created event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species updated event with current state and changed fields.
    /// Used for all update operations including deprecation, restoration, and realm changes.
    /// </summary>
    private async Task PublishSpeciesUpdatedEventAsync(SpeciesModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.PublishSpeciesUpdatedEventAsync");
        try
        {
            var eventModel = new SpeciesUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan,
                MaturityAge = model.MaturityAge,
                TraitModifiers = model.TraitModifiers,
                RealmIds = model.RealmIds?.ToList(),
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.PublishSpeciesUpdatedAsync(eventModel, cancellationToken);
            _logger.LogDebug("Published species.updated event for {SpeciesId} with changed fields: {ChangedFields}",
                model.SpeciesId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.updated event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species deleted event with final state before deletion.
    /// </summary>
    private async Task PublishSpeciesDeletedEventAsync(SpeciesModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.PublishSpeciesDeletedEventAsync");
        try
        {
            var eventModel = new SpeciesDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan,
                MaturityAge = model.MaturityAge,
                TraitModifiers = model.TraitModifiers,
                RealmIds = model.RealmIds?.ToList(),
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                DeletedReason = deletedReason
            };

            await _messageBus.PublishSpeciesDeletedAsync(eventModel, cancellationToken);
            _logger.LogDebug("Published species.deleted event for {SpeciesId}", model.SpeciesId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.deleted event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species merged event when one species is merged into another.
    /// </summary>
    private async Task PublishSpeciesMergedEventAsync(
        SpeciesModel sourceModel,
        SpeciesModel targetModel,
        int migratedCharacterCount,
        List<Guid> failedEntityIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.species", "SpeciesService.PublishSpeciesMergedEventAsync");
        try
        {
            var eventModel = new SpeciesMergedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SourceSpeciesId = sourceModel.SpeciesId,
                SourceSpeciesCode = sourceModel.Code,
                TargetSpeciesId = targetModel.SpeciesId,
                TargetSpeciesCode = targetModel.Code,
                MergedCharacterCount = migratedCharacterCount,
                FailedEntityIds = failedEntityIds.Count > 0 ? failedEntityIds : null
            };

            await _messageBus.PublishSpeciesMergedAsync(eventModel, cancellationToken);
            _logger.LogDebug("Published species.merged event: {SourceId} -> {TargetId} ({Count} characters)",
                sourceModel.SpeciesId, targetModel.SpeciesId, migratedCharacterCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.merged event for {SourceId} -> {TargetId}",
                sourceModel.SpeciesId, targetModel.SpeciesId);
        }
    }

    #endregion
}
