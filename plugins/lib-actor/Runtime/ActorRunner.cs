using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor.Handlers;
using BeyondImmersion.BannouService.Actor.Providers;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Core actor runtime that executes behavior loops with bounded channels for perception/message queues.
/// Each actor instance has one runner that manages its lifecycle.
/// </summary>
public sealed class ActorRunner : IActorRunner
{
    private readonly ILogger<ActorRunner> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly IMeshInvocationClient _meshClient;
    private readonly ActorTemplateData _template;
    private readonly ActorServiceConfiguration _config;
    private readonly Channel<PerceptionData> _perceptionQueue;
    private readonly ActorState _state;
    private readonly IStateStore<ActorStateSnapshot> _stateStore;
    private readonly IBehaviorDocumentLoader _behaviorLoader;
    private readonly IEnumerable<IVariableProviderFactory> _providerFactories;
    private readonly IDocumentExecutor _executor;
    private readonly IExpressionEvaluator _expressionEvaluator;

    private AbmlDocument? _behavior;
    private IReadOnlyList<GoapGoal>? _goapGoals;
    private IReadOnlyList<GoapAction>? _goapActions;
    private ActorStatus _status = ActorStatus.Pending;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _loopIterations;
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastStateSave;
    private bool _disposed;
    private readonly object _statusLock = new();

    // NPC Brain integration: subscription to character perception events and source tracking
    private IAsyncDisposable? _perceptionSubscription;
    private volatile string? _lastSourceAppId;

    // Event Brain encounter state (for actors managing encounters)
    private EncounterStateData? _encounter;

    // Node identity for event publishing
    private string NodeId => _config.PoolNodeId ?? _config.LocalModeNodeId;

    // Event topics
    private const string ACTOR_INSTANCE_STARTED_TOPIC = "actor.instance.started";
    private const string ACTOR_STATE_PERSISTED_TOPIC = "actor.instance.state-persisted";
    private const string ENCOUNTER_STARTED_TOPIC = "actor.encounter.started";
    private const string ENCOUNTER_ENDED_TOPIC = "actor.encounter.ended";
    private const string ENCOUNTER_PHASE_CHANGED_TOPIC = "actor.encounter.phase-changed";

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

    /// <inheritdoc/>
    public Guid? CurrentEncounterId => _encounter?.EncounterId;

    /// <summary>
    /// Creates a new actor runner instance.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="template">The template this actor was spawned from.</param>
    /// <param name="characterId">Optional character ID for NPC brain actors.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="messageSubscriber">Message subscriber for dynamic subscriptions.</param>
    /// <param name="meshClient">Mesh client for routing state updates to game servers.</param>
    /// <param name="stateStore">State store for actor persistence.</param>
    /// <param name="behaviorLoader">Behavior document loader.</param>
    /// <param name="providerFactories">Variable provider factories for ABML expressions (discovered via DI).</param>
    /// <param name="executor">Document executor for behavior execution.</param>
    /// <param name="expressionEvaluator">Expression evaluator for options evaluation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialState">Initial state snapshot, or null for fresh actor.</param>
    public ActorRunner(
        string actorId,
        ActorTemplateData template,
        Guid? characterId,
        ActorServiceConfiguration config,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IMeshInvocationClient meshClient,
        IStateStore<ActorStateSnapshot> stateStore,
        IBehaviorDocumentLoader behaviorLoader,
        IEnumerable<IVariableProviderFactory> providerFactories,
        IDocumentExecutor executor,
        IExpressionEvaluator expressionEvaluator,
        ILogger<ActorRunner> logger,
        object? initialState)
    {
        ActorId = actorId;
        _template = template;
        CharacterId = characterId;
        _config = config;
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _meshClient = meshClient;
        _stateStore = stateStore;
        _behaviorLoader = behaviorLoader;
        _providerFactories = providerFactories;
        _executor = executor;
        _expressionEvaluator = expressionEvaluator;
        _logger = logger;

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
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ActorRunner));

        if (Status != ActorStatus.Pending && Status != ActorStatus.Stopped)
        {
            _logger.LogWarning("Actor {ActorId} cannot start from status {Status}", ActorId, Status);
            return;
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

        // Setup perception subscription for NPC brain actors (character-addressed)
        if (CharacterId.HasValue)
        {
            await SetupPerceptionSubscriptionAsync(cancellationToken);
        }

        Status = ActorStatus.Running;
        _logger.LogInformation("Actor {ActorId} started successfully", ActorId);

        // Publish started event
        await _messageBus.TryPublishAsync(ACTOR_INSTANCE_STARTED_TOPIC, new ActorInstanceStartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = ActorId,
            NodeId = NodeId,
            TemplateId = TemplateId,
            CharacterId = CharacterId,
            Category = Category
        }, cancellationToken: cancellationToken);
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
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.ActorStopTimeoutSeconds));
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

        // Clean up perception subscriptions
        if (_perceptionSubscription != null)
        {
            try
            {
                await _perceptionSubscription.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Actor {ActorId} error disposing perception subscription", ActorId);
            }
            _perceptionSubscription = null;
        }

        Status = ActorStatus.Stopped;
        _logger.LogInformation("Actor {ActorId} stopped (iterations: {Iterations})", ActorId, LoopIterations);
    }

    /// <inheritdoc/>
    public bool InjectPerception(PerceptionData perception)
    {

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
            WorkingMemory = _state.GetAllWorkingMemory(),
            Encounter = _encounter
        };
    }

    /// <inheritdoc/>
    public bool StartEncounter(Guid encounterId, string encounterType, IReadOnlyList<Guid> participants, Dictionary<string, object?>? initialData = null)
    {
        if (_disposed)
            return false;

        // Cannot start a new encounter if one is already active
        if (_encounter != null)
        {
            _logger.LogWarning("Actor {ActorId} attempted to start encounter {EncounterId} but encounter {ActiveEncounterId} is already active",
                ActorId, encounterId, _encounter.EncounterId);
            return false;
        }

        _encounter = new EncounterStateData
        {
            EncounterId = encounterId,
            EncounterType = encounterType,
            Participants = new List<Guid>(participants),
            Phase = "initializing",
            StartedAt = DateTimeOffset.UtcNow,
            Data = initialData ?? new Dictionary<string, object?>()
        };

        _logger.LogInformation("Actor {ActorId} started encounter {EncounterId} (type: {Type}, participants: {Count})",
            ActorId, encounterId, encounterType, participants.Count);

        // Publish encounter started event (fire-and-forget: sync interface method)
        _ = _messageBus.TryPublishAsync(ENCOUNTER_STARTED_TOPIC, new ActorEncounterStartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = ActorId,
            EncounterId = encounterId,
            EncounterType = encounterType,
            Participants = participants.ToList()
        });

        return true;
    }

    /// <inheritdoc/>
    public bool SetEncounterPhase(string phase)
    {
        if (_disposed || _encounter == null)
            return false;

        var oldPhase = _encounter.Phase;
        _encounter.Phase = phase;

        _logger.LogDebug("Actor {ActorId} encounter {EncounterId} phase changed: {OldPhase} -> {NewPhase}",
            ActorId, _encounter.EncounterId, oldPhase, phase);

        // Publish phase changed event (fire-and-forget: sync interface method)
        _ = _messageBus.TryPublishAsync(ENCOUNTER_PHASE_CHANGED_TOPIC, new ActorEncounterPhaseChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = ActorId,
            EncounterId = _encounter.EncounterId,
            PreviousPhase = oldPhase,
            NewPhase = phase
        });

        return true;
    }

    /// <inheritdoc/>
    public bool EndEncounter()
    {
        if (_disposed || _encounter == null)
            return false;

        var encounterId = _encounter.EncounterId;
        var finalPhase = _encounter.Phase;
        var duration = DateTimeOffset.UtcNow - _encounter.StartedAt;
        _encounter = null;

        _logger.LogInformation("Actor {ActorId} ended encounter {EncounterId} (duration: {Duration})",
            ActorId, encounterId, duration);

        // Publish encounter ended event (fire-and-forget: sync interface method)
        _ = _messageBus.TryPublishAsync(ENCOUNTER_ENDED_TOPIC, new ActorEncounterEndedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = ActorId,
            EncounterId = encounterId,
            DurationSeconds = (float)duration.TotalSeconds,
            FinalPhase = finalPhase
        });

        return true;
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

        // Ensure perception subscriptions are cleaned up (belt-and-suspenders)
        if (_perceptionSubscription != null)
        {
            try
            {
                await _perceptionSubscription.DisposeAsync();
            }
            catch (Exception)
            {
                // Swallow - we're disposing
            }
            _perceptionSubscription = null;
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

                await _messageBus.TryPublishErrorAsync(
                    "actor",
                    "RunBehaviorLoop",
                    ex.GetType().Name,
                    ex.Message,
                    details: new { ActorId, LoopIterations },
                    stack: ex.StackTrace);

                // Continue running unless we're cancelled
                if (!ct.IsCancellationRequested)
                {
                    Status = ActorStatus.Error;
                    // Wait a bit before retrying
                    try
                    {
                        await Task.Delay(_config.ErrorRetryDelayMs, ct);
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
            if (perception.Urgency < (float)_config.PerceptionFilterThreshold)
            {
                // Skip very low urgency perceptions
                continue;
            }

            // Store in working memory with a structured key
            var key = $"perception:{perception.PerceptionType}:{perception.SourceId}";
            _state.SetWorkingMemory(key, perception);

            // Assess significance and potentially store as memory
            if (perception.Urgency >= (float)_config.PerceptionMemoryThreshold)
            {
                // High urgency perceptions become memories - store the full perception
                // for proper serialization (anonymous objects don't serialize correctly)
                _state.AddMemory(
                    $"recent:{perception.PerceptionType}",
                    perception,
                    DateTimeOffset.UtcNow.AddMinutes(_config.ShortTermMemoryMinutes)); // Short-term memory
            }

            processedCount++;
        }

        if (processedCount > 0)
        {
            _logger.LogDebug("Actor {ActorId} processed {Count} perceptions", ActorId, processedCount);
        }

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
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
                _behavior = await _behaviorLoader.GetDocumentAsync(_template.BehaviorRef, ct);
                _logger.LogInformation("Actor {ActorId} loaded behavior from {BehaviorRef}",
                    ActorId, _template.BehaviorRef);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Actor {ActorId} failed to load behavior from {BehaviorRef}",
                    ActorId, _template.BehaviorRef);
                await _messageBus.TryPublishErrorAsync(
                    "actor",
                    "LoadBehavior",
                    ex.GetType().Name,
                    ex.Message,
                    details: new { ActorId, BehaviorRef = _template.BehaviorRef },
                    stack: ex.StackTrace);
                return;
            }

            // Verify behavior was loaded (loader might return null without throwing)
            if (_behavior == null)
            {
                _logger.LogWarning("Actor {ActorId} behavior loader returned null for {BehaviorRef}",
                    ActorId, _template.BehaviorRef);
                return;
            }

            // Extract and cache GOAP metadata from behavior document (one-time on load)
            if (GoapMetadataConverter.HasGoapContent(_behavior))
            {
                (_goapGoals, _goapActions) = GoapMetadataConverter.ExtractAll(_behavior);
                _logger.LogInformation(
                    "Actor {ActorId} extracted GOAP metadata: {GoalCount} goals, {ActionCount} actions",
                    ActorId, _goapGoals.Count, _goapActions.Count);
            }
        }

        // 2. Create scope with current actor state
        var scope = await CreateExecutionScopeAsync(ct);

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

        // 5. Evaluate options block if present and store in state.memories
        if (_behavior.Options != null)
        {
            EvaluateAndStoreOptions(_behavior.Options, scope);
        }

        _logger.LogDebug("Actor {ActorId} completed behavior tick ({Flow})", ActorId, startFlow);
    }

    /// <summary>
    /// Evaluates the options block from the behavior document and stores results in actor state.
    /// Options are stored in state.memories.{type}_options with computed timestamp.
    /// </summary>
    private void EvaluateAndStoreOptions(OptionsDefinition options, IVariableScope scope)
    {
        var evaluated = OptionsEvaluator.EvaluateAll(options, scope, _expressionEvaluator, _logger);

        foreach (var (optionType, evaluatedOptions) in evaluated)
        {
            // Store as JSON in memories for retrieval via query-options endpoint
            var memoryKey = $"{optionType}_options";
            var memoryValue = new Dictionary<string, object?>
            {
                ["options"] = evaluatedOptions.Options.Select(o => new Dictionary<string, object?>
                {
                    ["actionId"] = o.ActionId,
                    ["preference"] = o.Preference,
                    ["risk"] = o.Risk,
                    ["available"] = o.Available,
                    ["requirements"] = o.Requirements,
                    ["cooldownMs"] = o.CooldownMs,
                    ["tags"] = o.Tags
                }).ToList(),
                ["computedAt"] = evaluatedOptions.ComputedAt.ToString("o"),
                ["count"] = evaluatedOptions.Options.Count
            };

            _state.AddMemory(memoryKey, memoryValue);

            _logger.LogDebug("Actor {ActorId} stored {Count} {OptionType} options",
                ActorId, evaluatedOptions.Options.Count, optionType);
        }
    }

    /// <summary>
    /// Creates a variable scope populated with current actor state for behavior execution.
    /// </summary>
    private async Task<VariableScope> CreateExecutionScopeAsync(CancellationToken ct)
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

        // Template configuration merged with actor service GOAP parameters
        var templateConfig = _template.Configuration as IDictionary<string, object?>;
        var mergedConfig = templateConfig != null
            ? new Dictionary<string, object?>(templateConfig)
            : new Dictionary<string, object?>();
        mergedConfig["goap_replan_threshold"] = _config.GoapReplanThreshold;
        mergedConfig["goap_max_plan_depth"] = _config.GoapMaxPlanDepth;
        mergedConfig["goap_plan_timeout_ms"] = _config.GoapPlanTimeoutMs;
        scope.SetValue("config", mergedConfig);

        // GOAP planning data (if behavior has GOAP content)
        if (_goapGoals != null && _goapActions != null)
        {
            scope.SetValue("goap_goals", _goapGoals);
            scope.SetValue("goap_actions", _goapActions);

            // Build world state from actor's current state (feelings as numeric properties)
            var worldState = BuildWorldStateFromActorState(goals);
            scope.SetValue("world_state", worldState);

            // Determine current goal from actor's primary goal (lookup in extracted goals)
            var primaryGoalName = goals.PrimaryGoal;
            if (!string.IsNullOrEmpty(primaryGoalName))
            {
                var currentGoal = _goapGoals.FirstOrDefault(g =>
                    g.Name.Equals(primaryGoalName, StringComparison.OrdinalIgnoreCase));
                scope.SetValue("current_goal", currentGoal);
            }
        }

        // Current perceptions (collected from queue this tick)
        scope.SetValue("perceptions", CollectCurrentPerceptions());

        // Encounter state for Event Brain actors
        if (_encounter != null)
        {
            scope.SetValue("encounter", new Dictionary<string, object?>
            {
                ["id"] = _encounter.EncounterId,
                ["type"] = _encounter.EncounterType,
                ["participants"] = _encounter.Participants.Select(p => p.ToString()).ToList(),
                ["phase"] = _encounter.Phase,
                ["started_at"] = _encounter.StartedAt.ToString("o"),
                ["data"] = _encounter.Data
            });
        }
        else
        {
            // No active encounter
            scope.SetValue("encounter", null);
        }

        // Load variable providers via registered factories (dependency inversion pattern).
        // Higher-layer services (L3/L4) register their provider factories with DI,
        // Actor (L2) discovers and uses them without knowing about specific game features.
        foreach (var factory in _providerFactories)
        {
            try
            {
                var provider = await factory.CreateAsync(CharacterId, ct);
                scope.RegisterProvider(provider);
                _logger.LogDebug("Actor {ActorId} registered provider {ProviderName}", ActorId, factory.ProviderName);
            }
            catch (Exception ex)
            {
                // Log but don't fail - actor can continue without optional providers
                _logger.LogWarning(ex, "Actor {ActorId} failed to create provider {ProviderName}",
                    ActorId, factory.ProviderName);
            }
        }

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
    /// Builds a GOAP WorldState from the actor's current state.
    /// Populates numeric properties from feelings and goal parameters.
    /// ABML behaviors can augment this via set: before calling trigger_goap_replan.
    /// </summary>
    /// <param name="goals">Current actor goals.</param>
    /// <returns>WorldState populated from actor state.</returns>
    private WorldState BuildWorldStateFromActorState(GoalStateData goals)
    {
        var worldState = new WorldState();

        // Add all feelings as numeric properties (e.g., hunger: 0.7, fear: 0.3)
        foreach (var (name, value) in _state.GetAllFeelings())
        {
            worldState = worldState.SetNumeric(name, (float)value);
        }

        // Add goal parameters as additional properties
        // These may include context like location, target_id, etc.
        if (goals.GoalParameters != null)
        {
            foreach (var (key, value) in goals.GoalParameters)
            {
                if (value == null) continue;

                worldState = value switch
                {
                    float f => worldState.SetNumeric(key, f),
                    double d => worldState.SetNumeric(key, (float)d),
                    int i => worldState.SetNumeric(key, i),
                    bool b => worldState.SetBoolean(key, b),
                    string s => worldState.SetString(key, s),
                    _ => worldState.SetValue(key, value)
                };
            }
        }

        // Add relevant working memory flags (booleans for state flags)
        var workingMemory = _state.GetAllWorkingMemory();
        foreach (var (key, value) in workingMemory)
        {
            // Skip perception entries (they're handled separately)
            if (key.StartsWith("perception:", StringComparison.Ordinal)) continue;

            // Add state flags (e.g., in_combat, has_weapon, etc.)
            if (value is bool b)
            {
                worldState = worldState.SetBoolean(key, b);
            }
            else if (value is string s)
            {
                worldState = worldState.SetString(key, s);
            }
        }

        return worldState;
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
                            : _config.DefaultMemoryExpirationMinutes;

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
    /// Routes directly to the game server via lib-mesh if we have a source app-id,
    /// otherwise falls back to pub/sub.
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

        var targetAppId = _lastSourceAppId;
        if (!string.IsNullOrEmpty(targetAppId))
        {
            // Route directly to game server via lib-mesh (no response expected)
            try
            {
                await _meshClient.InvokeMethodAsync(
                    targetAppId,
                    "character/state-update",
                    evt,
                    ct);
                _logger.LogDebug("Actor {ActorId} sent state update to {AppId} for character {CharacterId}",
                    ActorId, targetAppId, CharacterId);
            }
            catch (MeshInvocationException ex)
            {
                _logger.LogWarning(ex, "Actor {ActorId} failed to send state update to {AppId}, falling back to pub/sub",
                    ActorId, targetAppId);
                // Fall back to pub/sub
                await _messageBus.TryPublishAsync("character.state_update", evt, cancellationToken: ct);
            }
        }
        else
        {
            // No source app-id yet, use pub/sub fallback
            await _messageBus.TryPublishAsync("character.state_update", evt, cancellationToken: ct);
            _logger.LogDebug("Actor {ActorId} published state update via pub/sub for character {CharacterId} (no source app-id)",
                ActorId, CharacterId);
        }

        _state.ClearPendingChanges();
    }

    /// <summary>
    /// Persists actor state to the state store.
    /// Called on auto-save interval and shutdown.
    /// </summary>
    private async Task PersistStateAsync(CancellationToken ct)
    {
        var snapshot = GetStateSnapshot();

        for (int attempt = 0; attempt <= _config.MemoryStoreMaxRetries; attempt++)
        {
            try
            {
                await _stateStore.SaveAsync(ActorId, snapshot, cancellationToken: ct);
                _logger.LogDebug("Actor {ActorId} persisted state", ActorId);

                // Publish state persisted event
                await _messageBus.TryPublishAsync(ACTOR_STATE_PERSISTED_TOPIC, new ActorStatePersistedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ActorId = ActorId,
                    NodeId = NodeId,
                    LoopIterations = LoopIterations
                }, cancellationToken: ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _config.MemoryStoreMaxRetries)
            {
                _logger.LogWarning(ex, "Actor {ActorId} state persist attempt {Attempt} failed, retrying",
                    ActorId, attempt + 1);
                await Task.Delay(_config.StatePersistenceRetryDelayMs * (attempt + 1), ct);
            }
            catch (Exception ex)
            {
                // Final attempt failed - log but don't throw, persistence failure shouldn't kill the actor
                _logger.LogError(ex, "Actor {ActorId} failed to persist state after {MaxRetries} retries",
                    ActorId, _config.MemoryStoreMaxRetries);
                await _messageBus.TryPublishErrorAsync(
                    "actor",
                    "PersistState",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "state",
                    details: new { ActorId, Retries = _config.MemoryStoreMaxRetries },
                    stack: ex.StackTrace);
            }
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

    /// <summary>
    /// Sets up a dynamic subscription to receive perception events for this actor's character.
    /// Subscribes to character.{characterId}.perceptions topic using topic exchange.
    /// </summary>
    private async Task SetupPerceptionSubscriptionAsync(CancellationToken ct)
    {
        if (!CharacterId.HasValue)
            return;

        var topic = $"character.{CharacterId.Value}.perceptions";
        _logger.LogInformation("Actor {ActorId} subscribing to {Topic}", ActorId, topic);

        try
        {
            _perceptionSubscription = await _messageSubscriber.SubscribeDynamicAsync<CharacterPerceptionEvent>(
                topic,
                async (evt, innerCt) => await HandlePerceptionEventAsync(evt, innerCt),
                exchangeType: SubscriptionExchangeType.Topic,
                cancellationToken: ct);

            _logger.LogDebug("Actor {ActorId} subscribed to perception stream for character {CharacterId}",
                ActorId, CharacterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Actor {ActorId} failed to subscribe to perception stream for character {CharacterId}",
                ActorId, CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "SetupPerceptionSubscription",
                ex.GetType().Name,
                ex.Message,
                details: new { ActorId, CharacterId });
            // Don't throw - actor can still function without perception subscription
        }
    }

    /// <summary>
    /// Handles a perception event received from the message bus.
    /// Tracks the source app-id for routing state updates back, then injects the perception.
    /// </summary>
    private async Task HandlePerceptionEventAsync(CharacterPerceptionEvent evt, CancellationToken ct)
    {
        // Track source app-id for routing state updates back to the game server
        _lastSourceAppId = evt.SourceAppId;

        // Convert to PerceptionData and inject into the queue
        // Use PerceptionData which has proper enum types per IMPLEMENTATION TENETS (Type Safety)
        var perception = new PerceptionData
        {
            PerceptionType = evt.Perception.PerceptionType,
            SourceId = evt.Perception.SourceId,
            SourceType = evt.Perception.SourceType,
            Data = evt.Perception.Data,
            Urgency = evt.Perception.Urgency
        };

        InjectPerception(perception);

        _logger.LogDebug("Actor {ActorId} received perception from {SourceAppId} (type: {Type})",
            ActorId, evt.SourceAppId, perception.PerceptionType);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }
}
