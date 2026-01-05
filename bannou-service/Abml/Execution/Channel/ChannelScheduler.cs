// ═══════════════════════════════════════════════════════════════════════════
// ABML Channel Scheduler
// Cooperative scheduling for multi-channel execution.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Abml.Execution.Channel;

/// <summary>
/// Configuration for channel scheduler timeouts.
/// </summary>
public sealed class ChannelSchedulerConfig
{
    /// <summary>Maximum total execution time for all channels.</summary>
    public TimeSpan GlobalTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum time any single wait_for can block.</summary>
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum cycles before detecting potential infinite loop.</summary>
    public int MaxCycles { get; init; } = 10_000;

    /// <summary>Default configuration.</summary>
    public static ChannelSchedulerConfig Default => new();
}

/// <summary>
/// Cooperative scheduler for multi-channel ABML execution.
/// </summary>
public sealed class ChannelScheduler
{
    private readonly ChannelSchedulerConfig _config;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IActionHandlerRegistry _handlers;
    private readonly ConcurrentDictionary<string, ChannelState> _channels = new();
    private readonly ConcurrentDictionary<string, SyncPoint> _syncPoints = new();
    private readonly List<LogEntry> _logs = [];

    /// <summary>
    /// Creates a new channel scheduler.
    /// </summary>
    public ChannelScheduler(
        IExpressionEvaluator evaluator,
        IActionHandlerRegistry handlers,
        ChannelSchedulerConfig? config = null)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _config = config ?? ChannelSchedulerConfig.Default;
    }

    /// <summary>
    /// Executes a document with multiple channels.
    /// </summary>
    public async ValueTask<ChannelExecutionResult> ExecuteAsync(
        AbmlDocument document,
        IReadOnlyDictionary<string, string> channelFlows,
        IVariableScope? rootScope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(channelFlows);

        if (channelFlows.Count == 0)
        {
            return ChannelExecutionResult.Failure("No channels specified");
        }

        var scope = rootScope ?? new VariableScope();
        var startTime = DateTime.UtcNow;

        // Initialize channels
        foreach (var (channelName, flowName) in channelFlows)
        {
            if (!document.Flows.TryGetValue(flowName, out var flow))
            {
                return ChannelExecutionResult.Failure($"Flow not found: {flowName}");
            }

            var channelState = new ChannelState(channelName, scope);
            channelState.CallStack.Push(flowName, channelState.Scope);
            _channels[channelName] = channelState;
        }

        // Execute round-robin until all channels complete
        var cycleCount = 0;
        while (HasRunnableChannels())
        {
            ct.ThrowIfCancellationRequested();

            // Check global timeout
            if (DateTime.UtcNow - startTime > _config.GlobalTimeout)
            {
                return ChannelExecutionResult.Failure($"Global timeout exceeded ({_config.GlobalTimeout})", _logs);
            }

            // Check cycle limit
            if (++cycleCount > _config.MaxCycles)
            {
                return ChannelExecutionResult.Failure($"Max cycles exceeded ({_config.MaxCycles})", _logs);
            }

            // Process each runnable channel
            foreach (var channel in _channels.Values.Where(c => c.Status == ChannelStatus.Ready))
            {
                var result = await StepChannelAsync(document, channel, ct);
                if (result == StepResult.Error)
                {
                    return ChannelExecutionResult.Failure(
                        channel.ErrorMessage ?? "Unknown error",
                        _logs,
                        channel.Name);
                }
            }

            // Check waiting channels for timeouts and signal delivery
            CheckWaitingChannels();

            // Deadlock detection: all channels waiting with no signals
            if (DetectDeadlock())
            {
                return ChannelExecutionResult.Failure("Deadlock detected: all channels waiting", _logs);
            }
        }

        // Collect results
        var channelResults = _channels.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ReturnValue);

        return ChannelExecutionResult.Success(channelResults, _logs);
    }

    private bool HasRunnableChannels() =>
        _channels.Values.Any(c => c.Status is ChannelStatus.Ready or ChannelStatus.Waiting);

    private bool DetectDeadlock()
    {
        var channels = _channels.Values.ToList();

        // If all non-completed channels are waiting, it's a deadlock
        var activeChannels = channels.Where(c => c.Status != ChannelStatus.Completed).ToList();
        if (activeChannels.Count == 0)
        {
            return false;  // All done, not a deadlock
        }

        return activeChannels.All(c => c.Status == ChannelStatus.Waiting);
    }

    private void CheckWaitingChannels()
    {
        foreach (var channel in _channels.Values.Where(c => c.Status == ChannelStatus.Waiting))
        {
            // Check for timeout
            if (channel.WaitStartTime.HasValue &&
                DateTime.UtcNow - channel.WaitStartTime > _config.WaitTimeout)
            {
                channel.Status = ChannelStatus.Failed;
                channel.ErrorMessage = $"Wait timeout exceeded for signal: {channel.WaitingFor}";
                continue;
            }

            // Check if waiting for sync point
            if (channel.WaitingFor != null && channel.WaitingFor.StartsWith("__sync:"))
            {
                var syncPointName = channel.WaitingFor.Substring(7);  // Remove "__sync:" prefix
                if (_syncPoints.TryGetValue(syncPointName, out var syncPoint) &&
                    syncPoint.ChannelsArrived.Count >= syncPoint.TotalChannels)
                {
                    // All channels arrived, release this one
                    channel.Status = ChannelStatus.Ready;
                    channel.WaitingFor = null;
                    channel.WaitStartTime = null;
                    continue;
                }
            }

            // Check if signal arrived
            if (channel.WaitingFor != null && TryDeliverSignal(channel))
            {
                channel.Status = ChannelStatus.Ready;
                channel.WaitingFor = null;
                channel.WaitStartTime = null;
            }
        }
    }

    private bool TryDeliverSignal(ChannelState channel)
    {
        if (channel.WaitingFor == null) return false;

        // Check pending signals
        while (channel.PendingSignals.TryPeek(out var signal))
        {
            if (signal.Name == channel.WaitingFor)
            {
                channel.PendingSignals.TryDequeue(out _);

                // Set _signal variable in channel scope
                if (channel.Scope is VariableScope vs)
                {
                    vs.SetLocalValue("_signal", new Dictionary<string, object?>
                    {
                        ["name"] = signal.Name,
                        ["payload"] = signal.Payload,
                        ["source"] = signal.SourceChannel
                    });
                }
                return true;
            }
            channel.PendingSignals.TryDequeue(out _);  // Skip non-matching signal
        }

        return false;
    }

    private async ValueTask<StepResult> StepChannelAsync(
        AbmlDocument document,
        ChannelState channel,
        CancellationToken ct)
    {
        var frame = channel.CallStack.Current;
        if (frame == null)
        {
            channel.Status = ChannelStatus.Completed;
            return StepResult.Completed;
        }

        if (!document.Flows.TryGetValue(frame.FlowName, out var flow))
        {
            channel.Status = ChannelStatus.Failed;
            channel.ErrorMessage = $"Flow not found: {frame.FlowName}";
            return StepResult.Error;
        }

        // Check if we've finished all actions in this flow
        if (channel.ActionIndex >= flow.Actions.Count)
        {
            channel.CallStack.Pop();
            channel.ActionIndex = 0;

            if (channel.CallStack.Depth == 0)
            {
                channel.Status = ChannelStatus.Completed;
                return StepResult.Completed;
            }
            return StepResult.Continue;
        }

        var action = flow.Actions[channel.ActionIndex];
        channel.ActionIndex++;

        // Special handling for channel-specific actions
        if (action is EmitAction emitAction)
        {
            return HandleEmit(channel, emitAction);
        }
        if (action is WaitForAction waitAction)
        {
            return HandleWaitFor(channel, waitAction);
        }
        if (action is SyncAction syncAction)
        {
            return HandleSync(channel, syncAction);
        }

        // Standard action execution
        var context = CreateContextForChannel(document, channel);
        var handler = _handlers.GetHandler(action);
        if (handler == null)
        {
            channel.Status = ChannelStatus.Failed;
            channel.ErrorMessage = $"No handler for action: {action.GetType().Name}";
            return StepResult.Error;
        }

        var result = await handler.ExecuteAsync(action, context, ct);

        // Collect logs from action execution
        foreach (var log in context.Logs)
        {
            _logs.Add(log);
        }

        switch (result)
        {
            case ErrorResult errorResult:
                channel.Status = ChannelStatus.Failed;
                channel.ErrorMessage = errorResult.Message;
                return StepResult.Error;

            case GotoResult gotoResult:
                // Handle goto by transferring to target flow
                if (!document.Flows.TryGetValue(gotoResult.FlowName, out var targetFlow))
                {
                    channel.Status = ChannelStatus.Failed;
                    channel.ErrorMessage = $"Flow not found: {gotoResult.FlowName}";
                    return StepResult.Error;
                }
                // Pop current flow and push target
                channel.CallStack.Pop();
                var gotoScope = channel.Scope.CreateChild();
                if (gotoResult.Args != null)
                {
                    foreach (var (key, value) in gotoResult.Args)
                    {
                        gotoScope.SetValue(key, value);
                    }
                }
                channel.CallStack.Push(gotoResult.FlowName, gotoScope);
                channel.ActionIndex = 0;
                return StepResult.Continue;

            case ReturnResult returnResult:
                channel.ReturnValue = returnResult.Value;
                channel.Status = ChannelStatus.Completed;
                return StepResult.Completed;

            case CompleteResult completeResult:
                channel.ReturnValue = completeResult.Value;
                channel.Status = ChannelStatus.Completed;
                return StepResult.Completed;

            default:
                return StepResult.Continue;
        }
    }

    private StepResult HandleEmit(ChannelState channel, EmitAction emit)
    {
        // Evaluate payload if present
        object? payload = null;
        if (emit.Payload != null)
        {
            payload = _evaluator.Evaluate(emit.Payload, channel.Scope);
        }

        var signal = new ChannelSignal(emit.Signal, payload, channel.Name, DateTime.UtcNow);

        // Deliver to all other channels
        foreach (var targetChannel in _channels.Values.Where(c => c.Name != channel.Name))
        {
            targetChannel.PendingSignals.Enqueue(signal);
        }

        _logs.Add(new LogEntry("channel", $"[{channel.Name}] emit: {emit.Signal}", DateTime.UtcNow));
        return StepResult.Continue;
    }

    private StepResult HandleWaitFor(ChannelState channel, WaitForAction waitFor)
    {
        // Check if signal already pending
        if (channel.PendingSignals.Any(s => s.Name == waitFor.Signal))
        {
            TryDeliverSignal(channel);
            return StepResult.Continue;
        }

        // Start waiting
        channel.Status = ChannelStatus.Waiting;
        channel.WaitingFor = waitFor.Signal;
        channel.WaitStartTime = DateTime.UtcNow;

        _logs.Add(new LogEntry("channel", $"[{channel.Name}] wait_for: {waitFor.Signal}", DateTime.UtcNow));
        return StepResult.Yielded;
    }

    private StepResult HandleSync(ChannelState channel, SyncAction sync)
    {
        var pointName = sync.Point;

        if (!_syncPoints.TryGetValue(pointName, out var syncPoint))
        {
            syncPoint = new SyncPoint(pointName, _channels.Count);
            _syncPoints[pointName] = syncPoint;
        }

        syncPoint.ChannelsArrived.Add(channel.Name);

        if (syncPoint.ChannelsArrived.Count >= syncPoint.TotalChannels)
        {
            // All channels arrived, release them
            _logs.Add(new LogEntry("channel", $"Sync point '{pointName}' released", DateTime.UtcNow));
            return StepResult.Continue;
        }

        // Wait for other channels
        channel.Status = ChannelStatus.Waiting;
        channel.WaitingFor = $"__sync:{pointName}";
        channel.WaitStartTime = DateTime.UtcNow;

        _logs.Add(new LogEntry("channel", $"[{channel.Name}] sync: {pointName} ({syncPoint.ChannelsArrived.Count}/{syncPoint.TotalChannels})", DateTime.UtcNow));
        return StepResult.Yielded;
    }

    private ExecutionContext CreateContextForChannel(AbmlDocument document, ChannelState channel)
    {
        return new ExecutionContext
        {
            Document = document,
            RootScope = channel.Scope,
            Evaluator = _evaluator,
            Handlers = _handlers
        };
    }
}

/// <summary>
/// Internal step result.
/// </summary>
internal enum StepResult
{
    Continue,
    Yielded,
    Completed,
    Error
}

/// <summary>
/// Synchronization point for barrier synchronization.
/// </summary>
internal sealed class SyncPoint
{
    public string Name { get; }
    public int TotalChannels { get; }
    public HashSet<string> ChannelsArrived { get; } = [];

    public SyncPoint(string name, int totalChannels)
    {
        Name = name;
        TotalChannels = totalChannels;
    }
}

/// <summary>
/// Result of multi-channel execution.
/// </summary>
public sealed class ChannelExecutionResult
{
    /// <summary>Whether execution succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Channel that caused the error, if any.</summary>
    public string? FailedChannel { get; }

    /// <summary>Return values from each channel.</summary>
    public IReadOnlyDictionary<string, object?>? ChannelResults { get; }

    /// <summary>Logs collected during execution.</summary>
    public IReadOnlyList<LogEntry> Logs { get; }

    private ChannelExecutionResult(
        bool isSuccess,
        string? errorMessage,
        IReadOnlyDictionary<string, object?>? channelResults,
        IReadOnlyList<LogEntry>? logs,
        string? failedChannel)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ChannelResults = channelResults;
        Logs = logs ?? [];
        FailedChannel = failedChannel;
    }

    /// <summary>Creates a success result.</summary>
    public static ChannelExecutionResult Success(
        IReadOnlyDictionary<string, object?> results,
        IReadOnlyList<LogEntry>? logs = null) =>
        new(true, null, results, logs, null);

    /// <summary>Creates a failure result.</summary>
    public static ChannelExecutionResult Failure(
        string message,
        IReadOnlyList<LogEntry>? logs = null,
        string? failedChannel = null) =>
        new(false, message, null, logs, failedChannel);
}
