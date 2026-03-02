// =============================================================================
// Prefetch Snapshots Handler
// ABML action handler for batch-loading resource snapshots into cache.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles prefetch_snapshots actions by batch-loading resource snapshots into cache.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to prefetch multiple resource snapshots
/// before iterating, converting N sequential API calls into 1 batch call + N cache hits:
/// <code>
/// # YAML
/// - prefetch_snapshots:
///     resource_type: character
///     resource_ids: ${participants | map('id')}
///     filter:
///       - character-personality
///
/// - foreach:
///     variable: p
///     collection: ${participants}
///     do:
///       - load_snapshot:  # Cache hit - instant
///           name: char
///           resource_type: character
///           resource_id: ${p.id}
/// </code>
/// </para>
/// </remarks>
public sealed class PrefetchSnapshotsHandler : IActionHandler
{
    private readonly IResourceSnapshotCache _cache;
    private readonly ILogger<PrefetchSnapshotsHandler> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new prefetch snapshots handler.
    /// </summary>
    /// <param name="cache">Resource snapshot cache.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public PrefetchSnapshotsHandler(
        IResourceSnapshotCache cache,
        ILogger<PrefetchSnapshotsHandler> logger,
        ITelemetryProvider telemetryProvider)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is PrefetchSnapshotsAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "PrefetchSnapshotsHandler.ExecuteAsync");
        var prefetchAction = (PrefetchSnapshotsAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // 1. Evaluate resource_ids expression to get the list of GUIDs
            var resourceIdsValue = ValueEvaluator.Evaluate(
                prefetchAction.ResourceIds, scope, context.Evaluator);

            var resourceIds = ParseGuidList(resourceIdsValue);
            if (resourceIds == null)
            {
                _logger.LogWarning(
                    "prefetch_snapshots: resource_ids expression '{Expression}' did not evaluate to valid GUID list, got: {Value}",
                    prefetchAction.ResourceIds, resourceIdsValue);
                return ActionResult.Error(
                    $"prefetch_snapshots: resource_ids must evaluate to a list of GUIDs, got: {resourceIdsValue?.GetType().Name ?? "null"}");
            }

            if (resourceIds.Count == 0)
            {
                _logger.LogDebug(
                    "prefetch_snapshots: Empty resource_ids list for {ResourceType}, skipping",
                    prefetchAction.ResourceType);
                context.Logs.Add(new LogEntry(
                    "prefetch_snapshots",
                    $"Skipped (empty list) for {prefetchAction.ResourceType}",
                    DateTime.UtcNow));
                return ActionResult.Continue;
            }

            // 2. Prefetch all snapshots into cache
            var successCount = await _cache.PrefetchAsync(
                prefetchAction.ResourceType,
                resourceIds,
                prefetchAction.Filter,
                ct);

            _logger.LogDebug(
                "prefetch_snapshots: Prefetched {SuccessCount}/{TotalCount} {ResourceType} snapshots",
                successCount, resourceIds.Count, prefetchAction.ResourceType);

            // 3. Log for debugging
            context.Logs.Add(new LogEntry(
                "prefetch_snapshots",
                $"Prefetched {successCount}/{resourceIds.Count} {prefetchAction.ResourceType} snapshots",
                DateTime.UtcNow));

            return ActionResult.Continue;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Error("prefetch_snapshots: Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "prefetch_snapshots: Error prefetching snapshots for {ResourceType}",
                prefetchAction.ResourceType);
            return ActionResult.Error($"prefetch_snapshots: {ex.Message}");
        }
    }

    private static List<Guid>? ParseGuidList(object? value)
    {
        if (value == null)
        {
            return null;
        }

        // Handle IEnumerable of various types
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var result = new List<Guid>();
            foreach (var item in enumerable)
            {
                if (item is Guid guid)
                {
                    result.Add(guid);
                }
                else if (item is string str && Guid.TryParse(str, out var parsed))
                {
                    result.Add(parsed);
                }
                else
                {
                    // Invalid item in list
                    return null;
                }
            }
            return result;
        }

        return null;
    }
}
