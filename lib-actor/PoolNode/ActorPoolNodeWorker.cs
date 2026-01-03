// =============================================================================
// Actor Pool Node Worker
// BackgroundService that runs on pool nodes to execute actors.
// =============================================================================

using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.PoolNode;

/// <summary>
/// Background service that runs on pool nodes to manage actor lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deployment:</b> Runs when ACTOR_POOL_NODE_ID is set (pool node mode).
/// </para>
/// <para>
/// <b>Message Flow:</b>
/// <list type="bullet">
/// <item>Publishes actor.pool-node.registered on startup</item>
/// <item>Publishes actor.pool-node.heartbeat periodically (via HeartbeatEmitter)</item>
/// <item>Publishes actor.pool-node.draining on shutdown</item>
/// </list>
/// </para>
/// <para>
/// <b>Command Handling:</b> Spawn/stop commands are handled by ActorServiceEvents
/// which calls into this worker's HandleSpawnCommand and HandleStopCommand methods.
/// </para>
/// </remarks>
public sealed class ActorPoolNodeWorker : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IActorRegistry _actorRegistry;
    private readonly IActorRunnerFactory _actorRunnerFactory;
    private readonly HeartbeatEmitter _heartbeatEmitter;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ILogger<ActorPoolNodeWorker> _logger;

    /// <summary>
    /// Creates a new ActorPoolNodeWorker.
    /// </summary>
    public ActorPoolNodeWorker(
        IMessageBus messageBus,
        IActorRegistry actorRegistry,
        IActorRunnerFactory actorRunnerFactory,
        HeartbeatEmitter heartbeatEmitter,
        ActorServiceConfiguration configuration,
        ILogger<ActorPoolNodeWorker> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _actorRegistry = actorRegistry ?? throw new ArgumentNullException(nameof(actorRegistry));
        _actorRunnerFactory = actorRunnerFactory ?? throw new ArgumentNullException(nameof(actorRunnerFactory));
        _heartbeatEmitter = heartbeatEmitter ?? throw new ArgumentNullException(nameof(heartbeatEmitter));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nodeId = _configuration.PoolNodeId;
        var appId = _configuration.PoolNodeAppId;
        var poolType = _configuration.PoolNodeType;

        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(appId))
        {
            _logger.LogError("PoolNodeId and PoolNodeAppId must be configured for pool node mode");
            return;
        }

        _logger.LogInformation(
            "Starting actor pool node worker: NodeId={NodeId}, AppId={AppId}, PoolType={PoolType}",
            nodeId, appId, poolType);

        // Register with control plane
        await RegisterWithControlPlaneAsync(nodeId, appId, poolType, stoppingToken);

        // Start heartbeat emitter
        _heartbeatEmitter.Start();

        _logger.LogInformation("Actor pool node worker started, waiting for commands via event handlers");

        // Keep alive until shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        // Graceful shutdown
        await ShutdownAsync(nodeId);
    }

    private async Task RegisterWithControlPlaneAsync(
        string nodeId,
        string appId,
        string poolType,
        CancellationToken ct)
    {
        var registrationEvent = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = nodeId,
            AppId = appId,
            PoolType = poolType,
            Capacity = _configuration.PoolNodeCapacity
        };

        await _messageBus.TryPublishAsync("actor.pool-node.registered", registrationEvent, cancellationToken: ct);

        _logger.LogInformation(
            "Registered pool node {NodeId} with control plane (capacity: {Capacity})",
            nodeId, _configuration.PoolNodeCapacity);
    }

    /// <summary>
    /// Handles a spawn command from the control plane.
    /// Called by ActorServiceEvents when a spawn command is received.
    /// </summary>
    /// <param name="command">The spawn command.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if spawn succeeded, false otherwise.</returns>
    public async Task<bool> HandleSpawnCommandAsync(SpawnActorCommand command, CancellationToken ct)
    {
        _logger.LogInformation(
            "Received spawn command for actor {ActorId} (template: {TemplateId})",
            command.ActorId, command.TemplateId);

        try
        {
            // Create template data from command
            // Note: Category is not in SpawnActorCommand - use pool node type as fallback
            var template = new ActorTemplateData
            {
                TemplateId = command.TemplateId,
                BehaviorRef = command.BehaviorRef,
                Configuration = command.Configuration,
                Category = _configuration.PoolNodeType,
                TickIntervalMs = command.TickIntervalMs > 0 ? command.TickIntervalMs : 100
            };

            // Create and start the actor runner
            var runner = _actorRunnerFactory.Create(
                command.ActorId,
                template,
                command.CharacterId,
                command.Configuration);

            if (!_actorRegistry.TryRegister(command.ActorId, runner))
            {
                _logger.LogWarning("Actor {ActorId} already exists, skipping spawn", command.ActorId);
                return false;
            }

            await runner.StartAsync(ct);

            // Publish status changed event
            await PublishStatusChangedAsync(command.ActorId, "pending", "running", ct);

            _logger.LogInformation("Spawned actor {ActorId} successfully", command.ActorId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn actor {ActorId}", command.ActorId);

            // Publish error status
            await PublishStatusChangedAsync(command.ActorId, "pending", "error", ct);
            return false;
        }
    }

    /// <summary>
    /// Handles a stop command from the control plane.
    /// Called by ActorServiceEvents when a stop command is received.
    /// </summary>
    /// <param name="command">The stop command.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if stop succeeded, false otherwise.</returns>
    public async Task<bool> HandleStopCommandAsync(StopActorCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Received stop command for actor {ActorId}", command.ActorId);

        try
        {
            if (!_actorRegistry.TryGet(command.ActorId, out var runner) || runner == null)
            {
                _logger.LogWarning("Actor {ActorId} not found for stop command", command.ActorId);
                return false;
            }

            var previousStatus = runner.Status.ToString().ToLowerInvariant();

            await runner.StopAsync(command.Graceful, ct);

            if (!_actorRegistry.TryRemove(command.ActorId, out _))
            {
                _logger.LogWarning("Failed to remove actor {ActorId} from registry", command.ActorId);
            }

            // Publish status changed event
            await PublishStatusChangedAsync(command.ActorId, previousStatus, "stopped", ct);

            // Publish completed event
            var completedEvent = new ActorCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorId = command.ActorId,
                NodeId = _configuration.PoolNodeId ?? string.Empty,
                ExitReason = ActorCompletedEventExitReason.External_stop,
                ExitMessage = "Stopped via control plane command",
                LoopIterations = runner.LoopIterations,
                CharacterId = runner.CharacterId
            };

            await _messageBus.TryPublishAsync("actor.instance.completed", completedEvent, cancellationToken: ct);

            _logger.LogInformation("Stopped actor {ActorId} successfully", command.ActorId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop actor {ActorId}", command.ActorId);
            return false;
        }
    }

    private async Task PublishStatusChangedAsync(string actorId, string previousStatus, string newStatus, CancellationToken ct)
    {
        var statusEvent = new ActorStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            NodeId = _configuration.PoolNodeId ?? string.Empty,
            PreviousStatus = previousStatus,
            NewStatus = newStatus
        };

        await _messageBus.TryPublishAsync("actor.instance.status-changed", statusEvent, cancellationToken: ct);
    }

    private async Task ShutdownAsync(string nodeId)
    {
        _logger.LogInformation("Shutting down actor pool node worker");

        // Stop heartbeat emitter
        await _heartbeatEmitter.StopAsync();

        // Get remaining actors
        var runningActors = _actorRegistry.GetAllRunners().ToList();

        if (runningActors.Count > 0)
        {
            _logger.LogInformation("Draining {Count} actors before shutdown", runningActors.Count);

            // Publish draining event
            var drainingEvent = new PoolNodeDrainingEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                NodeId = nodeId,
                RemainingActors = runningActors.Count,
                EstimatedDrainTimeSeconds = runningActors.Count * 2 // Rough estimate
            };

            await _messageBus.TryPublishAsync("actor.pool-node.draining", drainingEvent);

            // Stop all actors gracefully
            foreach (var runner in runningActors)
            {
                try
                {
                    await runner.StopAsync(true);
                    _actorRegistry.TryRemove(runner.ActorId, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping actor {ActorId} during shutdown", runner.ActorId);
                }
            }
        }

        _logger.LogInformation("Actor pool node worker stopped");
    }
}
