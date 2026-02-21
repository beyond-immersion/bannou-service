// ═══════════════════════════════════════════════════════════════════════════
// ABML Channel State
// State management for individual execution channels.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Abml.Execution.Channel;

/// <summary>
/// Execution state for a single channel.
/// </summary>
public enum ChannelStatus
{
    /// <summary>Channel is ready to run.</summary>
    Ready,
    /// <summary>Channel is waiting for a signal.</summary>
    Waiting,
    /// <summary>Channel completed execution.</summary>
    Completed,
    /// <summary>Channel encountered an error.</summary>
    Failed
}

/// <summary>
/// State for a single execution channel.
/// </summary>
public sealed class ChannelState
{
    /// <summary>Channel name.</summary>
    public string Name { get; }

    /// <summary>Current execution status.</summary>
    public ChannelStatus Status { get; set; } = ChannelStatus.Ready;

    /// <summary>Channel-local variable scope.</summary>
    public IVariableScope Scope { get; }

    /// <summary>Call stack for this channel.</summary>
    public FlowStack CallStack { get; } = new();

    /// <summary>Pending signals received.</summary>
    public ConcurrentQueue<ChannelSignal> PendingSignals { get; } = new();

    /// <summary>Signal this channel is waiting for (if any).</summary>
    public string? WaitingFor { get; set; }

    /// <summary>When the channel started waiting (for timeout).</summary>
    public DateTime? WaitStartTime { get; set; }

    /// <summary>Current action index in the flow.</summary>
    public int ActionIndex { get; set; }

    /// <summary>Return value from channel execution.</summary>
    public object? ReturnValue { get; set; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a new channel state.
    /// </summary>
    /// <param name="name">Channel name.</param>
    /// <param name="parentScope">Parent scope to inherit from.</param>
    public ChannelState(string name, IVariableScope parentScope)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Scope = parentScope.CreateChild();
    }
}

/// <summary>
/// Signal sent between channels.
/// </summary>
/// <param name="Name">Signal name.</param>
/// <param name="Payload">Optional signal payload.</param>
/// <param name="SourceChannel">Channel that emitted the signal.</param>
/// <param name="Timestamp">When the signal was emitted.</param>
public sealed record ChannelSignal(
    string Name,
    object? Payload,
    string SourceChannel,
    DateTime Timestamp);
