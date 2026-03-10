#nullable enable

using System.Diagnostics;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Helpers;

/// <summary>
/// Static helper for Category B deprecation cleanup sweeps. Provides standardized
/// per-item error isolation, grace period evaluation, dry-run support, and logging
/// for the clean-deprecated endpoint pattern.
/// </summary>
/// <remarks>
/// Each Category B service calls this from its clean-deprecated endpoint method.
/// The helper handles orchestration; the service provides delegates for storage
/// access and event publishing (which are inherently service-specific).
/// </remarks>
public static class DeprecationCleanupHelper
{
    /// <summary>
    /// Executes a Category B deprecation cleanup sweep, removing deprecated entities
    /// with zero remaining instances after an optional grace period.
    /// </summary>
    /// <typeparam name="TEntity">The internal model type of the deprecated entity.</typeparam>
    /// <param name="deprecatedEntities">All currently deprecated entities to evaluate.</param>
    /// <param name="getEntityId">Extracts the entity's unique ID for logging and result reporting.</param>
    /// <param name="getDeprecatedAt">Extracts when the entity was deprecated (null = unknown, always eligible).</param>
    /// <param name="hasActiveInstancesAsync">Returns true if the entity still has active instances referencing it.</param>
    /// <param name="deleteAndPublishAsync">Deletes the entity from storage, removes indexes, and publishes the *.deleted event.</param>
    /// <param name="gracePeriodDays">Minimum days since deprecation before cleanup. 0 = immediate.</param>
    /// <param name="dryRun">If true, report eligible entities without deleting.</param>
    /// <param name="logger">Logger for structured output.</param>
    /// <param name="telemetryProvider">Telemetry provider for span creation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cleanup sweep result with counts and cleaned entity IDs.</returns>
    public static async Task<CleanupSweepResult> ExecuteCleanupSweepAsync<TEntity>(
        IReadOnlyList<TEntity> deprecatedEntities,
        Func<TEntity, Guid> getEntityId,
        Func<TEntity, DateTimeOffset?> getDeprecatedAt,
        Func<TEntity, CancellationToken, Task<bool>> hasActiveInstancesAsync,
        Func<TEntity, CancellationToken, Task> deleteAndPublishAsync,
        int gracePeriodDays,
        bool dryRun,
        ILogger logger,
        ITelemetryProvider telemetryProvider,
        CancellationToken ct)
    {
        using var activity = telemetryProvider.StartActivity(
            "bannou", "DeprecationCleanupHelper.ExecuteCleanupSweep");

        var cleaned = new List<Guid>();
        var remaining = 0;
        var errors = 0;
        var threshold = gracePeriodDays > 0
            ? DateTimeOffset.UtcNow.AddDays(-gracePeriodDays)
            : (DateTimeOffset?)null;

        foreach (var entity in deprecatedEntities)
        {
            var entityId = getEntityId(entity);
            try
            {
                // Grace period check: skip if deprecated too recently
                if (threshold.HasValue)
                {
                    var deprecatedAt = getDeprecatedAt(entity);
                    if (deprecatedAt == null || deprecatedAt > threshold)
                    {
                        remaining++;
                        continue;
                    }
                }

                // Instance check: skip if still referenced
                if (await hasActiveInstancesAsync(entity, ct))
                {
                    remaining++;
                    continue;
                }

                // Entity is eligible for cleanup
                if (!dryRun)
                {
                    await deleteAndPublishAsync(entity, ct);
                }
                cleaned.Add(entityId);
            }
            catch (Exception ex)
            {
                // Per-item error isolation per IMPLEMENTATION TENETS
                logger.LogWarning(ex, "Failed to clean deprecated entity {EntityId}, continuing", entityId);
                errors++;
            }
        }

        logger.LogInformation(
            "Deprecation cleanup {Mode}: {Cleaned} cleaned, {Remaining} remaining, {Errors} errors",
            dryRun ? "dry-run" : "executed",
            cleaned.Count,
            remaining,
            errors);

        return new CleanupSweepResult(cleaned.Count, remaining, errors, cleaned);
    }
}

/// <summary>
/// Result of a deprecation cleanup sweep. Mapped to the generated CleanDeprecatedResponse
/// by each service's clean-deprecated endpoint method.
/// </summary>
/// <param name="Cleaned">Number of deprecated entities removed (or eligible, if dry run).</param>
/// <param name="Remaining">Number of deprecated entities still having active instances or within grace period.</param>
/// <param name="Errors">Number of entities that failed during cleanup processing.</param>
/// <param name="CleanedIds">IDs of successfully cleaned (or eligible) entities.</param>
public record CleanupSweepResult(
    int Cleaned,
    int Remaining,
    int Errors,
    IReadOnlyList<Guid> CleanedIds);
