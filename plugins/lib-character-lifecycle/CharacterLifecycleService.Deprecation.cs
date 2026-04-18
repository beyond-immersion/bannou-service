using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Category B deprecation lifecycle for character-lifecycle's three template types
/// (LifecycleTemplate, HeritableTraitTemplate, HybridTraitTemplate). Partial class
/// per FOUNDATION TENETS — separates deprecation orchestration from the primary
/// interface method file and per-domain sub-method files.
/// </summary>
/// <remarks>
/// <para>
/// All three templates use composite string keys rather than Guids
/// (<c>speciesCode:gameServiceId</c> or <c>speciesA:speciesB:gameServiceId</c>), so
/// clean-deprecated sweeps return <see cref="CleanDeprecatedStringKeyResponse"/>
/// via <see cref="DeprecationCleanupHelper.ExecuteCleanupSweepAsync{TEntity}(
/// IReadOnlyList{TEntity}, Func{TEntity, string}, Func{TEntity, DateTimeOffset?},
/// Func{TEntity, CancellationToken, Task{bool}}, Func{TEntity, CancellationToken, Task},
/// int, bool, ILogger, ITelemetryProvider, CancellationToken)"/>. This matches Seed's
/// precedent for composite-keyed Category B templates.
/// </para>
/// <para>
/// Reverse-index maintenance for the <c>hasInstancesAsync</c> delegate is handled by
/// the instance-creation and cleanup paths (see <see cref="CharacterLifecycleService"/>'s
/// Seed/Cleanup methods). Key prefixes: <c>lifecycle-by-template:</c>,
/// <c>genetic-by-trait-template:</c>, <c>genetic-by-hybrid-template:</c>.
/// </para>
/// </remarks>
public partial class CharacterLifecycleService
{
    // ========================================================================
    // Deprecate (3 template types)
    // ========================================================================

    /// <summary>
    /// Deprecates a lifecycle template. Category B semantics: one-way, idempotent,
    /// no undeprecate, no per-entity delete. Existing LifecycleProfile instances
    /// continue to function; new profile creation referencing the deprecated
    /// template is rejected by <c>SeedLifecycleProfileAsync</c>.
    /// </summary>
    public async Task<(StatusCodes, GetLifecycleTemplateResponse?)> DeprecateLifecycleTemplateAsync(
        DeprecateLifecycleTemplateRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.DeprecateLifecycleTemplate");

        var key = BuildLifecycleTemplateKey(body.SpeciesCode, body.GameServiceId);
        var model = await _lifecycleTemplateStore.GetAsync(key, cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        // Idempotent per IMPLEMENTATION TENETS — caller's intent (deprecate) is already satisfied
        if (model.IsDeprecated)
            return (StatusCodes.OK, MapLifecycleTemplateToResponse(model));

        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;
        model.DeprecationReason = body.Reason;
        model.UpdatedAt = model.DeprecatedAt;

        await _lifecycleTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        // Publish as *.updated with changedFields per IMPLEMENTATION TENETS
        await _messageBus.PublishLifecycleTemplateUpdatedAsync(new LifecycleTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SpeciesCode = model.SpeciesCode,
            GameServiceId = model.GameServiceId,
            Stages = model.Stages.ToList(),
            NaturalDeathRange = model.NaturalDeathRange,
            FertilityWindow = model.FertilityWindow,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
        }, cancellationToken);

        _logger.LogInformation(
            "Deprecated lifecycle template for species {SpeciesCode} in game {GameServiceId}",
            model.SpeciesCode, model.GameServiceId);

        return (StatusCodes.OK, MapLifecycleTemplateToResponse(model));
    }

    /// <summary>
    /// Deprecates a heritable trait template. Category B semantics — see
    /// <see cref="DeprecateLifecycleTemplateAsync"/> for rules.
    /// </summary>
    public async Task<(StatusCodes, GetHeritableTraitTemplateResponse?)> DeprecateHeritableTraitTemplateAsync(
        DeprecateHeritableTraitTemplateRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.DeprecateHeritableTraitTemplate");

        var key = BuildTraitTemplateKey(body.SpeciesCode, body.GameServiceId);
        var model = await _traitTemplateStore.GetAsync(key, cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        if (model.IsDeprecated)
            return (StatusCodes.OK, MapHeritableTraitTemplateToResponse(model));

        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;
        model.DeprecationReason = body.Reason;
        model.UpdatedAt = model.DeprecatedAt;

        await _traitTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        await _messageBus.PublishHeritableTraitTemplateUpdatedAsync(new HeritableTraitTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SpeciesCode = model.SpeciesCode,
            GameServiceId = model.GameServiceId,
            Traits = model.Traits.ToList(),
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
        }, cancellationToken);

        _logger.LogInformation(
            "Deprecated heritable trait template for species {SpeciesCode} in game {GameServiceId}",
            model.SpeciesCode, model.GameServiceId);

        return (StatusCodes.OK, MapHeritableTraitTemplateToResponse(model));
    }

    /// <summary>
    /// Deprecates a hybrid trait template. Category B semantics — see
    /// <see cref="DeprecateLifecycleTemplateAsync"/> for rules.
    /// </summary>
    public async Task<(StatusCodes, GetHybridTraitTemplateResponse?)> DeprecateHybridTraitTemplateAsync(
        DeprecateHybridTraitTemplateRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.DeprecateHybridTraitTemplate");

        var key = BuildHybridTemplateKey(body.SpeciesA, body.SpeciesB, body.GameServiceId);
        var model = await _hybridTemplateStore.GetAsync(key, cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        if (model.IsDeprecated)
            return (StatusCodes.OK, MapHybridTraitTemplateToResponse(model));

        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;
        model.DeprecationReason = body.Reason;
        model.UpdatedAt = model.DeprecatedAt;

        await _hybridTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        await _messageBus.PublishHybridTraitTemplateUpdatedAsync(new HybridTraitTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SpeciesA = model.SpeciesA,
            SpeciesB = model.SpeciesB,
            GameServiceId = model.GameServiceId,
            TraitOverrides = model.TraitOverrides.ToList(),
            HybridFertilityModifier = model.HybridFertilityModifier,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
        }, cancellationToken);

        _logger.LogInformation(
            "Deprecated hybrid trait template for {SpeciesA}x{SpeciesB} in game {GameServiceId}",
            model.SpeciesA, model.SpeciesB, model.GameServiceId);

        return (StatusCodes.OK, MapHybridTraitTemplateToResponse(model));
    }

    // ========================================================================
    // Clean-Deprecated Sweeps (3 template types)
    // ========================================================================

    /// <summary>
    /// Category B cleanup sweep for deprecated lifecycle templates with zero
    /// remaining <c>LifecycleProfile</c> instances referencing them.
    /// </summary>
    public async Task<(StatusCodes, CleanDeprecatedStringKeyResponse?)> CleanDeprecatedLifecycleTemplatesAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.CleanDeprecatedLifecycleTemplates");

        var deprecated = await _queryableLifecycleTemplateStore.QueryAsync(
            t => t.IsDeprecated, cancellationToken);
        var deprecatedList = deprecated.ToList();

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedList,
            getEntityId: t => $"{t.SpeciesCode}:{t.GameServiceId}",
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: (t, ct) =>
                _profileStringStore.HasStringListEntriesAsync(
                    BuildLifecycleByTemplateKey(t.SpeciesCode, t.GameServiceId), ct),
            deleteAndPublishAsync: async (t, ct) =>
            {
                var templateKey = BuildLifecycleTemplateKey(t.SpeciesCode, t.GameServiceId);
                await _lifecycleTemplateStore.DeleteAsync(templateKey, ct);
                // Defensive cleanup of reverse index (should already be empty per hasInstancesAsync)
                await _profileStringStore.DeleteAsync(
                    BuildLifecycleByTemplateKey(t.SpeciesCode, t.GameServiceId), ct);

                await _messageBus.PublishLifecycleTemplateDeletedAsync(new LifecycleTemplateDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SpeciesCode = t.SpeciesCode,
                    GameServiceId = t.GameServiceId,
                    Stages = t.Stages.ToList(),
                    NaturalDeathRange = t.NaturalDeathRange,
                    FertilityWindow = t.FertilityWindow,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt ?? t.CreatedAt,
                    IsDeprecated = t.IsDeprecated,
                    DeprecatedAt = t.DeprecatedAt,
                    DeprecationReason = t.DeprecationReason,
                    DeletedReason = "clean-deprecated sweep"
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedStringKeyResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }

    /// <summary>
    /// Category B cleanup sweep for deprecated heritable trait templates with zero
    /// remaining non-hybrid <c>GeneticProfile</c> instances referencing them.
    /// </summary>
    public async Task<(StatusCodes, CleanDeprecatedStringKeyResponse?)> CleanDeprecatedHeritableTraitTemplatesAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.CleanDeprecatedHeritableTraitTemplates");

        var deprecated = await _queryableTraitTemplateStore.QueryAsync(
            t => t.IsDeprecated, cancellationToken);
        var deprecatedList = deprecated.ToList();

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedList,
            getEntityId: t => $"{t.SpeciesCode}:{t.GameServiceId}",
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: (t, ct) =>
                _heritageStringStore.HasStringListEntriesAsync(
                    BuildGeneticByTraitTemplateKey(t.SpeciesCode, t.GameServiceId), ct),
            deleteAndPublishAsync: async (t, ct) =>
            {
                var templateKey = BuildTraitTemplateKey(t.SpeciesCode, t.GameServiceId);
                await _traitTemplateStore.DeleteAsync(templateKey, ct);
                await _heritageStringStore.DeleteAsync(
                    BuildGeneticByTraitTemplateKey(t.SpeciesCode, t.GameServiceId), ct);

                await _messageBus.PublishHeritableTraitTemplateDeletedAsync(new HeritableTraitTemplateDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SpeciesCode = t.SpeciesCode,
                    GameServiceId = t.GameServiceId,
                    Traits = t.Traits.ToList(),
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt ?? t.CreatedAt,
                    IsDeprecated = t.IsDeprecated,
                    DeprecatedAt = t.DeprecatedAt,
                    DeprecationReason = t.DeprecationReason,
                    DeletedReason = "clean-deprecated sweep"
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedStringKeyResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }

    /// <summary>
    /// Category B cleanup sweep for deprecated hybrid trait templates with zero
    /// remaining hybrid <c>GeneticProfile</c> instances referencing them.
    /// </summary>
    public async Task<(StatusCodes, CleanDeprecatedStringKeyResponse?)> CleanDeprecatedHybridTraitTemplatesAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.CleanDeprecatedHybridTraitTemplates");

        var deprecated = await _queryableHybridTemplateStore.QueryAsync(
            t => t.IsDeprecated, cancellationToken);
        var deprecatedList = deprecated.ToList();

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedList,
            getEntityId: t => $"{t.SpeciesA}:{t.SpeciesB}:{t.GameServiceId}",
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: (t, ct) =>
                _heritageStringStore.HasStringListEntriesAsync(
                    BuildGeneticByHybridTemplateKey(t.SpeciesA, t.SpeciesB, t.GameServiceId), ct),
            deleteAndPublishAsync: async (t, ct) =>
            {
                var templateKey = BuildHybridTemplateKey(t.SpeciesA, t.SpeciesB, t.GameServiceId);
                await _hybridTemplateStore.DeleteAsync(templateKey, ct);
                await _heritageStringStore.DeleteAsync(
                    BuildGeneticByHybridTemplateKey(t.SpeciesA, t.SpeciesB, t.GameServiceId), ct);

                await _messageBus.PublishHybridTraitTemplateDeletedAsync(new HybridTraitTemplateDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SpeciesA = t.SpeciesA,
                    SpeciesB = t.SpeciesB,
                    GameServiceId = t.GameServiceId,
                    TraitOverrides = t.TraitOverrides.ToList(),
                    HybridFertilityModifier = t.HybridFertilityModifier,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt ?? t.CreatedAt,
                    IsDeprecated = t.IsDeprecated,
                    DeprecatedAt = t.DeprecatedAt,
                    DeprecationReason = t.DeprecationReason,
                    DeletedReason = "clean-deprecated sweep"
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedStringKeyResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }

    // ========================================================================
    // Internal Model → Response Mappers
    // ========================================================================

    private static GetLifecycleTemplateResponse MapLifecycleTemplateToResponse(LifecycleTemplateModel model) => new()
    {
        SpeciesCode = model.SpeciesCode,
        GameServiceId = model.GameServiceId,
        Stages = model.Stages.ToList(),
        NaturalDeathRange = model.NaturalDeathRange,
        FertilityWindow = model.FertilityWindow,
        IsDeprecated = model.IsDeprecated,
        DeprecatedAt = model.DeprecatedAt,
        DeprecationReason = model.DeprecationReason
    };

    private static GetHeritableTraitTemplateResponse MapHeritableTraitTemplateToResponse(HeritableTraitTemplateModel model) => new()
    {
        SpeciesCode = model.SpeciesCode,
        GameServiceId = model.GameServiceId,
        Traits = model.Traits.ToList(),
        IsDeprecated = model.IsDeprecated,
        DeprecatedAt = model.DeprecatedAt,
        DeprecationReason = model.DeprecationReason
    };

    private static GetHybridTraitTemplateResponse MapHybridTraitTemplateToResponse(HybridTraitTemplateModel model) => new()
    {
        SpeciesA = model.SpeciesA,
        SpeciesB = model.SpeciesB,
        GameServiceId = model.GameServiceId,
        TraitOverrides = model.TraitOverrides.ToList(),
        HybridFertilityModifier = model.HybridFertilityModifier,
        IsDeprecated = model.IsDeprecated,
        DeprecatedAt = model.DeprecatedAt,
        DeprecationReason = model.DeprecationReason
    };
}
