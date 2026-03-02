// =============================================================================
// Stop Watcher Handler
// ABML action handler for stopping regional watchers via Puppetmaster service.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles stop_watcher actions by invoking the Puppetmaster service to stop watchers.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to stop regional watchers dynamically:
/// <code>
/// # YAML
/// - stop_watcher:
///     watcher_id: ${watcher_to_stop}
/// </code>
/// </para>
/// <para>
/// The handler uses IPuppetmasterClient via mesh to call the Puppetmaster service,
/// supporting distributed deployment where actors run on different nodes than the
/// Puppetmaster service.
/// </para>
/// </remarks>
public sealed class StopWatcherHandler : IActionHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StopWatcherHandler> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new stop watcher handler.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public StopWatcherHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<StopWatcherHandler> logger,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is StopWatcherAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "StopWatcherHandler.ExecuteAsync");
        var stopAction = (StopWatcherAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // Evaluate watcher_id expression -> GUID
            var watcherIdValue = ValueEvaluator.Evaluate(
                stopAction.WatcherId, scope, context.Evaluator);

            if (!TryParseGuid(watcherIdValue, out var watcherId))
            {
                return ActionResult.Error(
                    $"stop_watcher: watcher_id must be a valid GUID, got: {watcherIdValue}");
            }

            // Call Puppetmaster service via mesh
            using var serviceScope = _scopeFactory.CreateScope();
            var puppetmasterClient = serviceScope.ServiceProvider.GetService<IPuppetmasterClient>();
            if (puppetmasterClient == null)
            {
                _logger.LogError("stop_watcher: IPuppetmasterClient not available");
                return ActionResult.Error("stop_watcher: Puppetmaster service not available");
            }

            var response = await puppetmasterClient.StopWatcherAsync(
                new StopWatcherRequest { WatcherId = watcherId },
                ct);

            _logger.LogDebug(
                "stop_watcher: Stopped watcher {WatcherId} (stopped: {Stopped})",
                watcherId, response?.Stopped);

            context.Logs.Add(new LogEntry(
                "stop_watcher",
                $"Stopped watcher {watcherId} (stopped: {response?.Stopped})",
                DateTime.UtcNow));

            return ActionResult.Continue;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Error("stop_watcher: Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "stop_watcher: Error stopping watcher");
            return ActionResult.Error($"stop_watcher: {ex.Message}");
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
