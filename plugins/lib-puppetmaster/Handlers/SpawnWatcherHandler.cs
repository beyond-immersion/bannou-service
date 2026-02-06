// =============================================================================
// Spawn Watcher Handler
// ABML action handler for spawning regional watchers via Puppetmaster service.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Puppetmaster.Handlers;

/// <summary>
/// Handles spawn_watcher actions by invoking the Puppetmaster service to start watchers.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables Event Brain actors to spawn regional watchers dynamically:
/// <code>
/// # YAML
/// - spawn_watcher:
///     watcher_type: regional
///     realm_id: ${event.realmId}
///     behavior_id: watcher-regional
///     into: spawned_watcher_id
/// </code>
/// </para>
/// <para>
/// The handler uses IPuppetmasterClient via mesh to call the Puppetmaster service,
/// supporting distributed deployment where actors run on different nodes than the
/// Puppetmaster service.
/// </para>
/// </remarks>
public sealed class SpawnWatcherHandler : IActionHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpawnWatcherHandler> _logger;

    /// <summary>
    /// Creates a new spawn watcher handler.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    public SpawnWatcherHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<SpawnWatcherHandler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is SpawnWatcherAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var spawnAction = (SpawnWatcherAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        try
        {
            // Evaluate watcher_type (required - already validated by parser)
            var watcherType = spawnAction.WatcherType;

            // Evaluate realm_id if provided (expression -> GUID)
            Guid? realmId = null;
            if (!string.IsNullOrEmpty(spawnAction.RealmId))
            {
                var realmIdValue = ValueEvaluator.Evaluate(
                    spawnAction.RealmId, scope, context.Evaluator);
                if (!TryParseGuid(realmIdValue, out var parsedRealmId))
                {
                    return ActionResult.Error(
                        $"spawn_watcher: realm_id must be a valid GUID, got: {realmIdValue}");
                }
                realmId = parsedRealmId;
            }

            // Evaluate behavior_id if provided (expression -> string)
            string? behaviorId = null;
            if (!string.IsNullOrEmpty(spawnAction.BehaviorId))
            {
                var behaviorIdValue = ValueEvaluator.Evaluate(
                    spawnAction.BehaviorId, scope, context.Evaluator);
                behaviorId = behaviorIdValue?.ToString();
            }

            // Validate realm_id is provided (required for StartWatcher)
            if (realmId == null)
            {
                return ActionResult.Error("spawn_watcher: realm_id is required");
            }

            // Call Puppetmaster service via mesh
            using var serviceScope = _scopeFactory.CreateScope();
            var puppetmasterClient = serviceScope.ServiceProvider.GetService<IPuppetmasterClient>();
            if (puppetmasterClient == null)
            {
                _logger.LogError("spawn_watcher: IPuppetmasterClient not available");
                return ActionResult.Error("spawn_watcher: Puppetmaster service not available");
            }

            var response = await puppetmasterClient.StartWatcherAsync(
                new StartWatcherRequest
                {
                    RealmId = realmId.Value,
                    WatcherType = watcherType,
                    BehaviorRef = behaviorId
                },
                ct);

            // Store watcher ID if 'into' variable specified
            if (!string.IsNullOrEmpty(spawnAction.Into) && response != null)
            {
                scope.SetValue(spawnAction.Into, response.Watcher.WatcherId);
            }

            _logger.LogDebug(
                "spawn_watcher: Started watcher {WatcherId} for realm {RealmId} (type: {WatcherType}, existed: {AlreadyExisted})",
                response?.Watcher.WatcherId, realmId, watcherType, response?.AlreadyExisted);

            context.Logs.Add(new LogEntry(
                "spawn_watcher",
                $"Started watcher {response?.Watcher.WatcherId} for realm {realmId}",
                DateTime.UtcNow));

            return ActionResult.Continue;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Error("spawn_watcher: Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "spawn_watcher: Error spawning watcher");
            return ActionResult.Error($"spawn_watcher: {ex.Message}");
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
