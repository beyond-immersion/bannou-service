// =============================================================================
// Watch Handler
// ABML action handler for registering resource change watches.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles watch actions by registering resource watches in the WatchRegistry.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to subscribe to resource change
/// notifications via the ABML <c>watch:</c> action:
/// <code>
/// # YAML
/// - watch:
///     resource_type: character
///     resource_id: ${target_id}
///     sources:
///       - character-personality
///       - character-history
/// </code>
/// </para>
/// <para>
/// When the watched resource changes (via lifecycle events matching the sources),
/// the Puppetmaster service injects a perception into the actor's bounded channel.
/// </para>
/// </remarks>
public sealed class WatchHandler : IActionHandler
{
    private readonly WatchRegistry _registry;
    private readonly ILogger<WatchHandler> _logger;

    /// <summary>
    /// Creates a new watch handler.
    /// </summary>
    /// <param name="registry">Watch registry.</param>
    /// <param name="logger">Logger instance.</param>
    public WatchHandler(
        WatchRegistry registry,
        ILogger<WatchHandler> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is WatchAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var watchAction = (WatchAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // 1. Evaluate resource_id expression to get the GUID
            var resourceIdValue = ValueEvaluator.Evaluate(
                watchAction.ResourceId, scope, context.Evaluator);

            if (!TryParseGuid(resourceIdValue, out var resourceId))
            {
                _logger.LogWarning(
                    "watch: resource_id expression '{Expression}' did not evaluate to valid GUID, got: {Value}",
                    watchAction.ResourceId, resourceIdValue);
                return ValueTask.FromResult(ActionResult.Error(
                    $"watch: resource_id must be a valid GUID, got: {resourceIdValue}"));
            }

            // 2. Get actor ID from context
            if (!context.ActorId.HasValue)
            {
                _logger.LogWarning("watch: Action executed outside actor context - no ActorId available");
                return ValueTask.FromResult(ActionResult.Error(
                    "watch: This action can only be executed within an actor context"));
            }

            var actorId = context.ActorId.Value;

            // 3. Register watch in registry
            _registry.AddWatch(actorId, watchAction.ResourceType, resourceId, watchAction.Sources, watchAction.OnChange);

            _logger.LogDebug(
                "watch: Actor {ActorId} watching {ResourceType}:{ResourceId} with sources: {Sources}, on_change: {OnChange}",
                actorId, watchAction.ResourceType, resourceId,
                watchAction.Sources != null ? string.Join(", ", watchAction.Sources) : "(all)",
                watchAction.OnChange ?? "(queue)");

            // 4. Log for debugging
            context.Logs.Add(new LogEntry(
                "watch",
                $"Registered watch on {watchAction.ResourceType}:{resourceId}",
                DateTime.UtcNow));

            return ValueTask.FromResult(ActionResult.Continue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "watch: Error registering watch for {ResourceType}",
                watchAction.ResourceType);
            return ValueTask.FromResult(ActionResult.Error($"watch: {ex.Message}"));
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
