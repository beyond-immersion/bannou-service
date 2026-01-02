using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Core actor runtime that executes behavior loops with bounded channels for perception/message queues.
/// Each actor instance has one runner that manages its lifecycle.
/// </summary>
public class ActorRunner : IActorRunner
{
    private readonly ILogger<ActorRunner> _logger;
    private readonly IMessageBus _messageBus;
    private readonly ActorTemplateData _template;
    private readonly ActorServiceConfiguration _config;
    private readonly Channel<PerceptionData> _perceptionQueue;
    private readonly ActorState _state;

    private ActorStatus _status = ActorStatus.Pending;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _loopIterations;
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastStateSave;
    private bool _disposed;
    private readonly object _statusLock = new();

    /// <inheritdoc/>
    public string ActorId { get; }

    /// <inheritdoc/>
    public Guid TemplateId => _template.TemplateId;

    /// <inheritdoc/>
    public string Category => _template.Category;

    /// <inheritdoc/>
    public Guid? CharacterId { get; }

    /// <inheritdoc/>
    public ActorStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
        private set
        {
            lock (_statusLock)
            {
                _status = value;
            }
        }
    }

    /// <inheritdoc/>
    public DateTimeOffset StartedAt => _startedAt;

    /// <inheritdoc/>
    public DateTimeOffset? LastHeartbeat => _lastHeartbeat;

    /// <inheritdoc/>
    public long LoopIterations => Interlocked.Read(ref _loopIterations);

    /// <inheritdoc/>
    public int PerceptionQueueDepth => _perceptionQueue.Reader.Count;

    /// <summary>
    /// Creates a new actor runner instance.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="template">The template this actor was spawned from.</param>
    /// <param name="characterId">Optional character ID for NPC brain actors.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialState">Optional initial state.</param>
    public ActorRunner(
        string actorId,
        ActorTemplateData template,
        Guid? characterId,
        ActorServiceConfiguration config,
        IMessageBus messageBus,
        ILogger<ActorRunner> logger,
        object? initialState = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        CharacterId = characterId;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create bounded perception queue with DropOldest behavior
        _perceptionQueue = Channel.CreateBounded<PerceptionData>(
            new BoundedChannelOptions(_config.PerceptionQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        _state = new ActorState();

        // Initialize state from initial state if provided
        if (initialState != null)
        {
            InitializeFromState(initialState);
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ActorRunner));

        if (Status != ActorStatus.Pending && Status != ActorStatus.Stopped)
        {
            _logger.LogWarning("Actor {ActorId} cannot start from status {Status}", ActorId, Status);
            return Task.CompletedTask;
        }

        Status = ActorStatus.Starting;
        _startedAt = DateTimeOffset.UtcNow;
        _lastStateSave = _startedAt;

        _logger.LogInformation("Starting actor {ActorId} (template: {TemplateId}, category: {Category})",
            ActorId, TemplateId, Category);

        // Create cancellation token for the loop
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start the behavior loop in background
        _loopTask = RunBehaviorLoopAsync(_loopCts.Token);

        Status = ActorStatus.Running;
        _logger.LogInformation("Actor {ActorId} started successfully", ActorId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(bool graceful = true, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        if (Status == ActorStatus.Stopped || Status == ActorStatus.Stopping)
            return;

        Status = ActorStatus.Stopping;
        _logger.LogInformation("Stopping actor {ActorId} (graceful: {Graceful})", ActorId, graceful);

        // Cancel the loop
        if (_loopCts != null)
        {
            await _loopCts.CancelAsync();
        }

        // Wait for the loop to complete (with timeout)
        if (_loopTask != null && graceful)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await _loopTask.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Actor {ActorId} stop timed out", ActorId);
            }
        }

        // Persist final state if auto-save is enabled
        if (_template.AutoSaveIntervalSeconds > 0)
        {
            await PersistStateAsync(cancellationToken);
        }

        Status = ActorStatus.Stopped;
        _logger.LogInformation("Actor {ActorId} stopped (iterations: {Iterations})", ActorId, LoopIterations);
    }

    /// <inheritdoc/>
    public bool InjectPerception(PerceptionData perception)
    {
        ArgumentNullException.ThrowIfNull(perception);

        if (_disposed || Status != ActorStatus.Running)
            return false;

        // TryWrite will succeed and drop oldest if queue is full
        return _perceptionQueue.Writer.TryWrite(perception);
    }

    /// <inheritdoc/>
    public ActorStateSnapshot GetStateSnapshot()
    {
        return new ActorStateSnapshot
        {
            ActorId = ActorId,
            TemplateId = TemplateId,
            Category = Category,
            CharacterId = CharacterId,
            Status = Status,
            StartedAt = StartedAt,
            LastHeartbeat = LastHeartbeat,
            LoopIterations = LoopIterations,
            PerceptionQueueDepth = PerceptionQueueDepth,
            Feelings = _state.GetAllFeelings(),
            Goals = _state.GetGoals(),
            Memories = _state.GetAllMemories(),
            WorkingMemory = _state.GetAllWorkingMemory()
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop if running
        if (Status == ActorStatus.Running)
        {
            await StopAsync(graceful: false);
        }

        _loopCts?.Dispose();
        _perceptionQueue.Writer.Complete();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Main behavior loop that runs until cancelled.
    /// </summary>
    private async Task RunBehaviorLoopAsync(CancellationToken ct)
    {
        var tickInterval = TimeSpan.FromMilliseconds(_template.TickIntervalMs > 0
            ? _template.TickIntervalMs
            : _config.DefaultTickIntervalMs);

        var autoSaveInterval = TimeSpan.FromSeconds(_template.AutoSaveIntervalSeconds > 0
            ? _template.AutoSaveIntervalSeconds
            : _config.DefaultAutoSaveIntervalSeconds);

        _logger.LogDebug("Actor {ActorId} behavior loop starting (tick: {TickMs}ms, autoSave: {SaveSecs}s)",
            ActorId, tickInterval.TotalMilliseconds, autoSaveInterval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            var startTime = Stopwatch.GetTimestamp();

            try
            {
                // 1. Process perceptions (drain queue into working memory)
                await ProcessPerceptionsAsync(ct);

                // 2. Execute behavior tick
                await ExecuteBehaviorTickAsync(ct);

                // 3. Publish state update if changed
                await PublishStateUpdateIfNeededAsync(ct);

                // 4. Periodic state persistence
                if (autoSaveInterval > TimeSpan.Zero && DateTimeOffset.UtcNow - _lastStateSave >= autoSaveInterval)
                {
                    await PersistStateAsync(ct);
                    _lastStateSave = DateTimeOffset.UtcNow;
                }

                // Update heartbeat and iteration count
                _lastHeartbeat = DateTimeOffset.UtcNow;
                Interlocked.Increment(ref _loopIterations);

                // 5. Cleanup expired memories
                _state.CleanupExpiredMemories();

                // 6. Sleep remainder of tick
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                if (elapsed < tickInterval)
                {
                    await Task.Delay(tickInterval - elapsed, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in actor {ActorId} behavior loop iteration {Iteration}",
                    ActorId, LoopIterations);

                // Continue running unless we're cancelled
                if (!ct.IsCancellationRequested)
                {
                    Status = ActorStatus.Error;
                    // Wait a bit before retrying
                    try
                    {
                        await Task.Delay(1000, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    Status = ActorStatus.Running;
                }
            }
        }

        _logger.LogDebug("Actor {ActorId} behavior loop ended after {Iterations} iterations",
            ActorId, LoopIterations);
    }

    /// <summary>
    /// Processes all queued perceptions into working memory.
    /// </summary>
    private async Task ProcessPerceptionsAsync(CancellationToken ct)
    {
        var processedCount = 0;

        while (_perceptionQueue.Reader.TryRead(out var perception))
        {
            // Apply attention filter (based on urgency)
            if (perception.Urgency < 0.1f)
            {
                // Skip very low urgency perceptions
                continue;
            }

            // Store in working memory with a structured key
            var key = $"perception:{perception.PerceptionType}:{perception.SourceId}";
            _state.SetWorkingMemory(key, perception);

            // Assess significance and potentially store as memory
            if (perception.Urgency >= 0.7f)
            {
                // High urgency perceptions become memories
                _state.AddMemory(
                    $"recent:{perception.PerceptionType}",
                    new { perception.SourceId, perception.SourceType, perception.Data, perception.Urgency },
                    DateTimeOffset.UtcNow.AddMinutes(5)); // Short-term memory
            }

            processedCount++;
        }

        if (processedCount > 0)
        {
            _logger.LogDebug("Actor {ActorId} processed {Count} perceptions", ActorId, processedCount);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Executes one tick of behavior.
    /// This is a placeholder - actual behavior execution would use IDocumentExecutor.
    /// </summary>
    private async Task ExecuteBehaviorTickAsync(CancellationToken ct)
    {
        // TODO: Integrate with behavior execution (IDocumentExecutor)
        // For now, this is a stub that demonstrates the flow

        // The behavior tree would:
        // 1. Read from working memory
        // 2. Make decisions based on goals and feelings
        // 3. Execute actions
        // 4. Update state (feelings, goals, memories)

        await Task.CompletedTask;
    }

    /// <summary>
    /// Publishes a CharacterStateUpdate event if there are pending changes.
    /// </summary>
    private async Task PublishStateUpdateIfNeededAsync(CancellationToken ct)
    {
        if (!_state.HasPendingChanges || CharacterId == null)
            return;

        var evt = new CharacterStateUpdateEvent
        {
            EventName = "character.state_update",
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = CharacterId.Value,
            ActorId = ActorId,
            Feelings = _state.GetPendingFeelingChanges(),
            Goals = _state.GetPendingGoalChanges(),
            Memories = _state.GetPendingMemoryChanges(),
            BehaviorChange = _state.GetPendingBehaviorChange()
        };

        await _messageBus.TryPublishAsync("character.state_update", evt, cancellationToken: ct);
        _state.ClearPendingChanges();

        _logger.LogDebug("Actor {ActorId} published state update for character {CharacterId}",
            ActorId, CharacterId);
    }

    /// <summary>
    /// Persists actor state (called on auto-save interval and shutdown).
    /// </summary>
    private async Task PersistStateAsync(CancellationToken ct)
    {
        // TODO: Implement state persistence via lib-state
        // For now, just log that we would persist
        _logger.LogDebug("Actor {ActorId} would persist state (not yet implemented)", ActorId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Initializes actor state from initial state object.
    /// </summary>
    private void InitializeFromState(object initialState)
    {
        // TODO: Parse initial state and apply to _state
        // This would handle loading saved state or initial configuration
        _logger.LogDebug("Actor {ActorId} initialized with initial state", ActorId);
    }
}
