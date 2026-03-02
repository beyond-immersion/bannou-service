// =============================================================================
// Load Snapshot Handler
// ABML action handler for loading resource snapshots into variable providers.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Puppetmaster.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles load_snapshot actions by loading resource snapshots and registering
/// them as variable providers for ABML expression evaluation.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to load character data (personality,
/// history, encounters, etc.) and access it via standard ABML expression syntax:
/// <code>
/// # YAML
/// - load_snapshot:
///     name: candidate
///     resource_type: character
///     resource_id: ${target_id}
///     filter:
///       - character-personality
///       - character-history
///
/// # Then access via expressions
/// - cond:
///     - when: ${candidate.personality.aggression > 0.7}
///       then: ...
/// </code>
/// </para>
/// <para>
/// The snapshot is registered in the document's root scope for document-wide access.
/// If the snapshot cannot be loaded, an empty provider is registered that returns
/// null for all paths (graceful degradation).
/// </para>
/// </remarks>
public sealed class LoadSnapshotHandler : IActionHandler
{
    private readonly IResourceSnapshotCache _cache;
    private readonly ILogger<LoadSnapshotHandler> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new load snapshot handler.
    /// </summary>
    /// <param name="cache">Resource snapshot cache.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public LoadSnapshotHandler(
        IResourceSnapshotCache cache,
        ILogger<LoadSnapshotHandler> logger,
        ITelemetryProvider telemetryProvider)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is LoadSnapshotAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "LoadSnapshotHandler.ExecuteAsync");
        var loadAction = (LoadSnapshotAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // 1. Evaluate resource_id expression to get the GUID
            var resourceIdValue = ValueEvaluator.Evaluate(
                loadAction.ResourceId, scope, context.Evaluator);

            if (!TryParseGuid(resourceIdValue, out var resourceId))
            {
                _logger.LogWarning(
                    "load_snapshot: resource_id expression '{Expression}' did not evaluate to valid GUID, got: {Value}",
                    loadAction.ResourceId, resourceIdValue);
                return ActionResult.Error(
                    $"load_snapshot: resource_id must be a valid GUID, got: {resourceIdValue}");
            }

            // 2. Determine effective filter:
            //    - Explicit filter from action takes priority
            //    - Fall back to behavior's declared resource_templates
            //    - null means no filtering
            IReadOnlyList<string>? effectiveFilter = loadAction.Filter;

            if (effectiveFilter == null || effectiveFilter.Count == 0)
            {
                var declaredTemplates = context.Document.Metadata.ResourceTemplates;
                if (declaredTemplates.Count > 0)
                {
                    effectiveFilter = declaredTemplates;
                    _logger.LogDebug(
                        "load_snapshot: Using declared resource_templates as filter: {Templates}",
                        string.Join(", ", declaredTemplates));
                }
            }

            // 3. Load snapshot from cache (handles Resource service calls internally)
            var snapshot = await _cache.GetOrLoadAsync(
                loadAction.ResourceType,
                resourceId,
                effectiveFilter,
                ct);

            // 4. Create and register provider
            ResourceArchiveProvider provider;
            if (snapshot == null)
            {
                _logger.LogDebug(
                    "load_snapshot: No snapshot found for {ResourceType}:{ResourceId}, registering empty provider '{Name}'",
                    loadAction.ResourceType, resourceId, loadAction.Name);
                provider = ResourceArchiveProvider.Empty(loadAction.Name);
            }
            else
            {
                _logger.LogDebug(
                    "load_snapshot: Loaded snapshot for {ResourceType}:{ResourceId} with {EntryCount} entries, registering as '{Name}'",
                    loadAction.ResourceType, resourceId, snapshot.Entries.Count, loadAction.Name);
                provider = new ResourceArchiveProvider(loadAction.Name, snapshot);
            }

            // 5. Register provider in root scope for document-wide access
            if (context.RootScope is VariableScope rootScope)
            {
                rootScope.RegisterProvider(provider);
            }
            else
            {
                _logger.LogWarning(
                    "load_snapshot: RootScope is not VariableScope, cannot register provider '{Name}'",
                    loadAction.Name);
                return ActionResult.Error(
                    "load_snapshot: Cannot register provider - unexpected scope type");
            }

            // 6. Log for debugging
            context.Logs.Add(new LogEntry(
                "load_snapshot",
                $"Registered '{loadAction.Name}' for {loadAction.ResourceType}:{resourceId}",
                DateTime.UtcNow));

            return ActionResult.Continue;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Error("load_snapshot: Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "load_snapshot: Error loading snapshot for {ResourceType}",
                loadAction.ResourceType);
            return ActionResult.Error($"load_snapshot: {ex.Message}");
        }
    }

    private static bool TryParseGuid(object? value, out Guid result)
    {
        if (value is Guid guid)
        {
            result = guid;
            return true;
        }

        if (value is string str && Guid.TryParse(str, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = Guid.Empty;
        return false;
    }
}
