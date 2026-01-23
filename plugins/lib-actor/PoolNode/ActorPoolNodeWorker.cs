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
/// <b>Command Subscriptions:</b> Subscribes to node-specific command topics:
/// <list type="bullet">
/// <item>actor.node.{appId}.spawn - SpawnActorCommand</item>
/// <item>actor.node.{appId}.stop - StopActorCommand</item>
/// <item>actor.node.{appId}.message - SendMessageCommand (routed to actor perception queue)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ActorPoolNodeWorker : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly IActorRegistry _actorRegistry;
    private readonly IActorRunnerFactory _actorRunnerFactory;
    private readonly HeartbeatEmitter _heartbeatEmitter;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ILogger<ActorPoolNodeWorker> _logger;

    // Subscriptions to be disposed on shutdown
    private readonly List<IAsyncDisposable> _subscriptions = new();

    /// <summary>
    /// Gets the validated PoolNodeId, throwing if not configured.
    /// ExecuteAsync validates this at startup, so this should never throw in normal operation.
    /// </summary>
    private string ValidatedNodeId => !string.IsNullOrEmpty(_configuration.PoolNodeId)
        ? _configuration.PoolNodeId
        : throw new InvalidOperationException("PoolNodeId must be configured for pool node mode - this indicates a bug in startup validation");

    /// <summary>
    /// Creates a new ActorPoolNodeWorker.
    /// </summary>
    public ActorPoolNodeWorker(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IActorRegistry actorRegistry,
        IActorRunnerFactory actorRunnerFactory,
        HeartbeatEmitter heartbeatEmitter,
        ActorServiceConfiguration configuration,
        ILogger<ActorPoolNodeWorker> logger)
    {
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _actorRegistry = actorRegistry;
        _actorRunnerFactory = actorRunnerFactory;
        _heartbeatEmitter = heartbeatEmitter;
        _configuration = configuration;
        _logger = logger;
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

        // Subscribe to command topics
        await SubscribeToCommandsAsync(appId, stoppingToken);

        // Register with control plane
        await RegisterWithControlPlaneAsync(nodeId, appId, poolType, stoppingToken);

        // Start heartbeat emitter
        _heartbeatEmitter.Start();

        _logger.LogInformation("Actor pool node worker started, listening for commands on actor.node.{AppId}.*", appId);

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
    /// Sets up subscriptions to node-specific command topics.
    /// </summary>
    private async Task SubscribeToCommandsAsync(string appId, CancellationToken ct)
    {
        // Subscribe to spawn commands
        var spawnSub = await _messageSubscriber.SubscribeDynamicAsync<SpawnActorCommand>(
            $"actor.node.{appId}.spawn",
            async (command, cancellationToken) =>
            {
                await HandleSpawnCommandAsync(command, cancellationToken);
            },
            cancellationToken: ct);
        _subscriptions.Add(spawnSub);

        // Subscribe to stop commands
        var stopSub = await _messageSubscriber.SubscribeDynamicAsync<StopActorCommand>(
            $"actor.node.{appId}.stop",
            async (command, cancellationToken) =>
            {
                await HandleStopCommandAsync(command, cancellationToken);
            },
            cancellationToken: ct);
        _subscriptions.Add(stopSub);

        // Subscribe to message commands (for routing messages to actors)
        var messageSub = await _messageSubscriber.SubscribeDynamicAsync<SendMessageCommand>(
            $"actor.node.{appId}.message",
            async (command, cancellationToken) =>
            {
                await HandleMessageCommandAsync(command, cancellationToken);
            },
            cancellationToken: ct);
        _subscriptions.Add(messageSub);

        _logger.LogDebug(
            "Subscribed to command topics: actor.node.{AppId}.spawn, actor.node.{AppId}.stop, actor.node.{AppId}.message",
            appId, appId, appId);
    }

    /// <summary>
    /// Handles a spawn command from the control plane.
    /// Called via subscription to actor.node.{appId}.spawn topic.
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
                TickIntervalMs = command.TickIntervalMs > 0 ? command.TickIntervalMs : _configuration.DefaultTickIntervalMs
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

            await _messageBus.TryPublishErrorAsync(
                "actor",
                "SpawnActor",
                ex.GetType().Name,
                ex.Message,
                details: new { command.ActorId, command.TemplateId },
                stack: ex.StackTrace,
                cancellationToken: ct);

            // Publish error status
            await PublishStatusChangedAsync(command.ActorId, "pending", "error", ct);
            return false;
        }
    }

    /// <summary>
    /// Handles a stop command from the control plane.
    /// Called via subscription to actor.node.{appId}.stop topic.
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
                NodeId = ValidatedNodeId,
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
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "StopActor",
                ex.GetType().Name,
                ex.Message,
                details: new { command.ActorId },
                stack: ex.StackTrace,
                cancellationToken: ct);
            return false;
        }
    }

    /// <summary>
    /// Handles a message command by routing it to the target actor's perception queue.
    /// Messages are converted to perceptions and injected into the actor's queue.
    /// </summary>
    /// <param name="command">The message command containing actorId, messageType, and payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if message was delivered, false if actor not found.</returns>
    public async Task<bool> HandleMessageCommandAsync(SendMessageCommand command, CancellationToken ct)
    {
        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        _logger.LogDebug(
            "Received message command for actor {ActorId} (type: {MessageType})",
            command.ActorId, command.MessageType);

        try
        {
            if (!_actorRegistry.TryGet(command.ActorId, out var runner) || runner == null)
            {
                _logger.LogWarning("Actor {ActorId} not found for message command", command.ActorId);
                return false;
            }

            // Convert the message to a perception and inject it
            var perception = new PerceptionData
            {
                PerceptionType = command.MessageType,
                SourceId = "message-bus",
                SourceType = "message",
                Data = command.Payload,
                Urgency = command.Urgency ?? 0.5f
            };

            var queued = runner.InjectPerception(perception);

            if (queued)
            {
                _logger.LogDebug(
                    "Delivered message to actor {ActorId} (type: {MessageType}, queueDepth: {Depth})",
                    command.ActorId, command.MessageType, runner.PerceptionQueueDepth);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to queue message for actor {ActorId} (actor not running or disposed)",
                    command.ActorId);
            }

            return queued;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message command for actor {ActorId}", command.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandleMessageCommand",
                ex.GetType().Name,
                ex.Message,
                details: new { command.ActorId, command.MessageType },
                stack: ex.StackTrace,
                cancellationToken: ct);
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
            NodeId = ValidatedNodeId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus
        };

        await _messageBus.TryPublishAsync("actor.instance.status-changed", statusEvent, cancellationToken: ct);
    }

    private async Task ShutdownAsync(string nodeId)
    {
        _logger.LogInformation("Shutting down actor pool node worker");

        // Dispose command subscriptions
        var subscriptionCount = _subscriptions.Count;
        foreach (var subscription in _subscriptions)
        {
            try
            {
                await subscription.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing subscription during shutdown");
            }
        }
        _subscriptions.Clear();
        _logger.LogDebug("Disposed {Count} command subscriptions", subscriptionCount);

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
                    await _messageBus.TryPublishErrorAsync(
                        "actor",
                        "ShutdownActor",
                        ex.GetType().Name,
                        ex.Message,
                        details: new { runner.ActorId, nodeId },
                        stack: ex.StackTrace);
                }
            }
        }

        _logger.LogInformation("Actor pool node worker stopped");
    }
}
