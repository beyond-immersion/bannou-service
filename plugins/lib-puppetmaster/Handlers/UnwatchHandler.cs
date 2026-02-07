// =============================================================================
// Unwatch Handler
// ABML action handler for removing resource change watches.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles unwatch actions by removing resource watches from the WatchRegistry.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to unsubscribe from resource change
/// notifications via the ABML <c>unwatch:</c> action:
/// <code>
/// # YAML
/// - unwatch:
///     resource_type: character
///     resource_id: ${target_id}
/// </code>
/// </para>
/// </remarks>
public sealed class UnwatchHandler : IActionHandler
{
    private readonly WatchRegistry _registry;
    private readonly ILogger<UnwatchHandler> _logger;

    /// <summary>
    /// Creates a new unwatch handler.
    /// </summary>
    /// <param name="registry">Watch registry.</param>
    /// <param name="logger">Logger instance.</param>
    public UnwatchHandler(
        WatchRegistry registry,
        ILogger<UnwatchHandler> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is UnwatchAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var unwatchAction = (UnwatchAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // 1. Evaluate resource_id expression to get the GUID
            var resourceIdValue = ValueEvaluator.Evaluate(
                unwatchAction.ResourceId, scope, context.Evaluator);

            if (!TryParseGuid(resourceIdValue, out var resourceId))
            {
                _logger.LogWarning(
                    "unwatch: resource_id expression '{Expression}' did not evaluate to valid GUID, got: {Value}",
                    unwatchAction.ResourceId, resourceIdValue);
                return ValueTask.FromResult(ActionResult.Error(
                    $"unwatch: resource_id must be a valid GUID, got: {resourceIdValue}"));
            }

            // 2. Get actor ID from context
            if (!context.ActorId.HasValue)
            {
                _logger.LogWarning("unwatch: Action executed outside actor context - no ActorId available");
                return ValueTask.FromResult(ActionResult.Error(
                    "unwatch: This action can only be executed within an actor context"));
            }

            var actorId = context.ActorId.Value;

            // 3. Remove watch from registry
            var removed = _registry.RemoveWatch(actorId, unwatchAction.ResourceType, resourceId);

            if (removed)
            {
                _logger.LogDebug(
                    "unwatch: Actor {ActorId} stopped watching {ResourceType}:{ResourceId}",
                    actorId, unwatchAction.ResourceType, resourceId);
            }
            else
            {
                _logger.LogDebug(
                    "unwatch: Actor {ActorId} was not watching {ResourceType}:{ResourceId} (no-op)",
                    actorId, unwatchAction.ResourceType, resourceId);
            }

            // 4. Log for debugging
            context.Logs.Add(new LogEntry(
                "unwatch",
                $"Removed watch on {unwatchAction.ResourceType}:{resourceId}",
                DateTime.UtcNow));

            return ValueTask.FromResult(ActionResult.Continue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "unwatch: Error removing watch for {ResourceType}",
                unwatchAction.ResourceType);
            return ValueTask.FromResult(ActionResult.Error($"unwatch: {ex.Message}"));
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
