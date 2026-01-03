using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Actor.Caching;
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
    private readonly IStateStore<ActorStateSnapshot> _stateStore;
    private readonly IBehaviorDocumentCache _behaviorCache;
    private readonly IDocumentExecutor _executor;

    private AbmlDocument? _behavior;
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
    /// <param name="stateStore">State store for actor persistence.</param>
    /// <param name="behaviorCache">Behavior document cache.</param>
    /// <param name="executor">Document executor for behavior execution.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialState">Optional initial state.</param>
    public ActorRunner(
        string actorId,
        ActorTemplateData template,
        Guid? characterId,
        ActorServiceConfiguration config,
        IMessageBus messageBus,
        IStateStore<ActorStateSnapshot> stateStore,
        IBehaviorDocumentCache behaviorCache,
        IDocumentExecutor executor,
        ILogger<ActorRunner> logger,
        object? initialState = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        CharacterId = characterId;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _behaviorCache = behaviorCache ?? throw new ArgumentNullException(nameof(behaviorCache));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
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
    /// Executes one tick of behavior using the loaded ABML document.
    /// </summary>
    private async Task ExecuteBehaviorTickAsync(CancellationToken ct)
    {
        // 1. Lazy-load behavior document on first tick
        if (_behavior == null)
        {
            if (string.IsNullOrWhiteSpace(_template.BehaviorRef))
            {
                _logger.LogDebug("Actor {ActorId} has no behavior reference, skipping tick", ActorId);
                return;
            }

            try
            {
                _behavior = await _behaviorCache.GetOrLoadAsync(_template.BehaviorRef, ct);
                _logger.LogInformation("Actor {ActorId} loaded behavior from {BehaviorRef}",
                    ActorId, _template.BehaviorRef);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Actor {ActorId} failed to load behavior from {BehaviorRef}",
                    ActorId, _template.BehaviorRef);
                return;
            }
        }

        // 2. Create scope with current actor state
        var scope = CreateExecutionScope();

        // 3. Execute behavior (prefer process_tick flow if available, otherwise main)
        var startFlow = _behavior.Flows.ContainsKey("process_tick") ? "process_tick" : "main";
        var result = await _executor.ExecuteAsync(_behavior, startFlow, scope, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Actor {ActorId} behavior tick failed: {Error}", ActorId, result.Error);
            return;
        }

        // 4. Apply state changes from scope back to ActorState
        ApplyStateChangesFromScope(scope);

        _logger.LogDebug("Actor {ActorId} completed behavior tick ({Flow})", ActorId, startFlow);
    }

    /// <summary>
    /// Creates a variable scope populated with current actor state for behavior execution.
    /// </summary>
    private VariableScope CreateExecutionScope()
    {
        var scope = new VariableScope();

        // Agent identity
        scope.SetValue("agent", new Dictionary<string, object?>
        {
            ["id"] = ActorId,
            ["behavior_id"] = _template.BehaviorRef,
            ["character_id"] = CharacterId?.ToString(),
            ["category"] = Category,
            ["template_id"] = TemplateId.ToString()
        });

        // Current emotional state
        scope.SetValue("feelings", _state.GetAllFeelings());

        // Current goals
        var goals = _state.GetGoals();
        scope.SetValue("goals", new Dictionary<string, object?>
        {
            ["primary"] = goals.PrimaryGoal,
            ["secondary"] = goals.SecondaryGoals,
            ["parameters"] = goals.GoalParameters
        });

        // Long-term memories
        scope.SetValue("memories", _state.GetAllMemories()
            .ToDictionary(m => m.MemoryKey, m => m.MemoryValue));

        // Working memory (perception-derived data)
        scope.SetValue("working_memory", _state.GetAllWorkingMemory());

        // Template configuration
        scope.SetValue("config", _template.Configuration);

        // Current perceptions (collected from queue this tick)
        scope.SetValue("perceptions", CollectCurrentPerceptions());

        return scope;
    }

    /// <summary>
    /// Collects current perceptions from working memory for behavior execution.
    /// </summary>
    private List<Dictionary<string, object?>> CollectCurrentPerceptions()
    {
        var perceptions = new List<Dictionary<string, object?>>();
        var workingMemory = _state.GetAllWorkingMemory();

        foreach (var (key, value) in workingMemory)
        {
            if (key.StartsWith("perception:", StringComparison.Ordinal) && value is PerceptionData pd)
            {
                perceptions.Add(new Dictionary<string, object?>
                {
                    ["type"] = pd.PerceptionType,
                    ["source_id"] = pd.SourceId,
                    ["source_type"] = pd.SourceType,
                    ["data"] = pd.Data,
                    ["urgency"] = pd.Urgency
                });
            }
        }

        return perceptions;
    }

    /// <summary>
    /// Applies state changes from behavior execution scope back to ActorState.
    /// Behavior flows set convention-named variables to indicate changes.
    /// </summary>
    private void ApplyStateChangesFromScope(IVariableScope scope)
    {
        // Apply feeling updates (convention: _feelings_update = { "feeling_name": value, ... })
        if (scope.HasVariable("_feelings_update"))
        {
            var feelingsUpdate = scope.GetValue("_feelings_update");
            if (feelingsUpdate is IReadOnlyDictionary<string, object?> feelings)
            {
                foreach (var (name, value) in feelings)
                {
                    if (value is double d)
                        _state.SetFeeling(name, d);
                    else if (value is float f)
                        _state.SetFeeling(name, f);
                    else if (value is int i)
                        _state.SetFeeling(name, i);
                }
            }
        }

        // Apply goal updates (convention: _goals_update = { primary: "...", secondary: [...], parameters: {...} })
        if (scope.HasVariable("_goals_update"))
        {
            var goalsUpdate = scope.GetValue("_goals_update");
            if (goalsUpdate is IReadOnlyDictionary<string, object?> goalsDict)
            {
                if (goalsDict.TryGetValue("primary", out var primary) && primary is string primaryGoal)
                {
                    var parameters = goalsDict.TryGetValue("parameters", out var p)
                        && p is IReadOnlyDictionary<string, object?> pDict
                        ? pDict.Where(kv => kv.Value != null)
                            .ToDictionary(kv => kv.Key, kv => kv.Value!)
                        : new Dictionary<string, object>();
                    _state.SetPrimaryGoal(primaryGoal, parameters);
                }

                if (goalsDict.TryGetValue("secondary", out var secondary)
                    && secondary is IEnumerable<object> secondaryGoals)
                {
                    foreach (var sg in secondaryGoals.OfType<string>())
                    {
                        _state.AddSecondaryGoal(sg);
                    }
                }
            }
        }

        // Apply memory additions (convention: _memories_add = [{ key: "...", value: {...}, expires_minutes: N }, ...])
        if (scope.HasVariable("_memories_add"))
        {
            var memoriesAdd = scope.GetValue("_memories_add");
            if (memoriesAdd is IEnumerable<object> memories)
            {
                foreach (var mem in memories)
                {
                    if (mem is IReadOnlyDictionary<string, object?> memDict)
                    {
                        var key = memDict.TryGetValue("key", out var k) ? k?.ToString() : null;
                        var value = memDict.TryGetValue("value", out var v) ? v : null;
                        var expiresMinutes = memDict.TryGetValue("expires_minutes", out var e) && e is double exp
                            ? (int)exp
                            : 60; // Default 1 hour

                        if (!string.IsNullOrEmpty(key))
                        {
                            _state.AddMemory(key, value, DateTimeOffset.UtcNow.AddMinutes(expiresMinutes));
                        }
                    }
                }
            }
        }

        // Apply behavior change (convention: _behavior_change = "new_behavior_name")
        if (scope.HasVariable("_behavior_change"))
        {
            var behaviorChange = scope.GetValue("_behavior_change");
            if (behaviorChange is string newBehavior && !string.IsNullOrWhiteSpace(newBehavior))
            {
                // Record behavior change: adding new behavior, removing old, with reason
                _state.RecordBehaviorChange(
                    added: new[] { newBehavior },
                    removed: null,
                    reason: "Behavior flow requested change");
                // Invalidate cached behavior so next tick loads new one
                _behavior = null;
            }
        }
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
    /// Persists actor state to the state store.
    /// Called on auto-save interval and shutdown.
    /// </summary>
    private async Task PersistStateAsync(CancellationToken ct)
    {
        var snapshot = GetStateSnapshot();

        try
        {
            await _stateStore.SaveAsync(ActorId, snapshot, cancellationToken: ct);
            _logger.LogDebug("Actor {ActorId} persisted state", ActorId);
        }
        catch (Exception ex)
        {
            // Log but don't throw - persistence failure shouldn't kill the actor
            _logger.LogError(ex, "Actor {ActorId} failed to persist state", ActorId);
        }
    }

    /// <summary>
    /// Initializes actor state from an ActorStateSnapshot.
    /// Called during construction if initial state is provided.
    /// </summary>
    private void InitializeFromState(object initialState)
    {
        if (initialState is not ActorStateSnapshot snapshot)
        {
            _logger.LogDebug("Actor {ActorId} received initial state of type {Type}, expected ActorStateSnapshot",
                ActorId, initialState.GetType().Name);
            return;
        }

        // Restore feelings
        if (snapshot.Feelings != null)
        {
            foreach (var (name, value) in snapshot.Feelings)
            {
                _state.SetFeeling(name, value);
            }
        }

        // Restore goals
        if (snapshot.Goals != null)
        {
            if (!string.IsNullOrEmpty(snapshot.Goals.PrimaryGoal))
            {
                var goalParams = snapshot.Goals.GoalParameters?
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!)
                    ?? new Dictionary<string, object>();
                _state.SetPrimaryGoal(snapshot.Goals.PrimaryGoal, goalParams);
            }

            if (snapshot.Goals.SecondaryGoals != null)
            {
                foreach (var secondary in snapshot.Goals.SecondaryGoals)
                {
                    _state.AddSecondaryGoal(secondary);
                }
            }
        }

        // Restore memories
        if (snapshot.Memories != null)
        {
            foreach (var memory in snapshot.Memories)
            {
                _state.AddMemory(memory.MemoryKey, memory.MemoryValue, memory.ExpiresAt);
            }
        }

        // Restore working memory
        if (snapshot.WorkingMemory != null)
        {
            foreach (var (key, value) in snapshot.WorkingMemory)
            {
                _state.SetWorkingMemory(key, value);
            }
        }

        // Clear pending changes since we just loaded saved state
        _state.ClearPendingChanges();

        _logger.LogDebug("Actor {ActorId} restored state from snapshot", ActorId);
    }
}
