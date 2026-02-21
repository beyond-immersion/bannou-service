// =============================================================================
// Watch Action Compilers
// Compilers for resource watch/unwatch actions.
// These are runtime-handled actions, not bytecode VM operations.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Actions;

/// <summary>
/// Compiles watch actions.
/// Watch actions subscribe to resource change notifications.
/// In bytecode, these emit a trace marker; actual subscription is handled by the runtime.
/// </summary>
public sealed class WatchCompiler : ActionCompilerBase<WatchAction>
{
    /// <summary>Creates a new watch compiler.</summary>
    public WatchCompiler()
    {
    }

    /// <inheritdoc/>
    protected override void CompileTyped(WatchAction action, CompilationContext context)
    {
        // Watch actions are runtime operations - the bytecode VM doesn't execute
        // these directly. The runtime layer (tree-walker or host environment)
        // intercepts and handles the actual subscription management.
        //
        // For bytecode compilation, we emit a trace marker so the action is
        // visible in debug output.

        var emitter = context.Emitter;

        // Emit trace marker for debugging
        var sources = action.Sources != null ? string.Join(",", action.Sources) : "*";
        var callback = action.OnChange != null ? $"→{action.OnChange}" : "";
        var traceMsg = $"[watch:{action.ResourceType}/{action.ResourceId}|{sources}{callback}]";
        var msgIdx = context.Strings.GetOrAdd(traceMsg);
        emitter.EmitTrace(msgIdx);
    }
}

/// <summary>
/// Compiles unwatch actions.
/// Unwatch actions unsubscribe from resource change notifications.
/// In bytecode, these emit a trace marker; actual unsubscription is handled by the runtime.
/// </summary>
public sealed class UnwatchCompiler : ActionCompilerBase<UnwatchAction>
{
    /// <summary>Creates a new unwatch compiler.</summary>
    public UnwatchCompiler()
    {
    }

    /// <inheritdoc/>
    protected override void CompileTyped(UnwatchAction action, CompilationContext context)
    {
        // Unwatch actions are runtime operations - see WatchCompiler for details.

        var emitter = context.Emitter;

        // Emit trace marker for debugging
        var traceMsg = $"[unwatch:{action.ResourceType}/{action.ResourceId}]";
        var msgIdx = context.Strings.GetOrAdd(traceMsg);
        emitter.EmitTrace(msgIdx);
    }
}
