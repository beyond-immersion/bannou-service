// =============================================================================
// List Watchers Handler
// ABML action handler for querying regional watchers via Puppetmaster service.
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
/// Handles list_watchers actions by querying the Puppetmaster service for active watchers.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to query active watchers:
/// <code>
/// # YAML
/// - list_watchers:
///     into: active_watchers
///     realm_id: ${realm_id}
///     watcher_type: regional
///
/// - foreach:
///     variable: w
///     collection: ${active_watchers}
///     do:
///       - log: { message: "Found watcher: ${w.watcherId}" }
/// </code>
/// </para>
/// <para>
/// The handler uses IPuppetmasterClient via mesh to call the Puppetmaster service,
/// supporting distributed deployment where actors run on different nodes than the
/// Puppetmaster service.
/// </para>
/// </remarks>
public sealed class ListWatchersHandler : IActionHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListWatchersHandler> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new list watchers handler.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public ListWatchersHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<ListWatchersHandler> logger,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is ListWatchersAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "ListWatchersHandler.ExecuteAsync");
        var listAction = (ListWatchersAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // Evaluate realm_id filter if provided (expression -> GUID)
            Guid? realmId = null;
            if (!string.IsNullOrEmpty(listAction.RealmId))
            {
                var realmIdValue = ValueEvaluator.Evaluate(
                    listAction.RealmId, scope, context.Evaluator);
                if (!TryParseGuid(realmIdValue, out var parsedRealmId))
                {
                    return ActionResult.Error(
                        $"list_watchers: realm_id must be a valid GUID, got: {realmIdValue}");
                }
                realmId = parsedRealmId;
            }

            // Call Puppetmaster service via mesh
            using var serviceScope = _scopeFactory.CreateScope();
            var puppetmasterClient = serviceScope.ServiceProvider.GetService<IPuppetmasterClient>();
            if (puppetmasterClient == null)
            {
                _logger.LogError("list_watchers: IPuppetmasterClient not available");
                return ActionResult.Error("list_watchers: Puppetmaster service not available");
            }

            var response = await puppetmasterClient.ListWatchersAsync(
                new ListWatchersRequest { RealmId = realmId },
                ct);

            // Filter by watcher_type if specified (client-side filter since API only supports realm filter)
            var watchers = response?.Watchers ?? [];
            if (!string.IsNullOrEmpty(listAction.WatcherType))
            {
                watchers = watchers
                    .Where(w => string.Equals(w.WatcherType, listAction.WatcherType, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Store results in the 'into' variable
            scope.SetValue(listAction.Into, watchers);

            _logger.LogDebug(
                "list_watchers: Found {Count} watchers (realm filter: {RealmId}, type filter: {WatcherType})",
                watchers.Count, realmId, listAction.WatcherType);

            context.Logs.Add(new LogEntry(
                "list_watchers",
                $"Found {watchers.Count} watchers",
                DateTime.UtcNow));

            return ActionResult.Continue;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Error("list_watchers: Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "list_watchers: Error listing watchers");
            return ActionResult.Error($"list_watchers: {ex.Message}");
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
