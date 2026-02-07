// =============================================================================
// Watch Action Compilers
// Compilers for resource watch/unwatch actions.
// These actions are handled at runtime by Puppetmaster, not the bytecode VM.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Expressions;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Actions;

/// <summary>
/// Compiles watch actions.
/// Watch actions subscribe to resource change notifications via Puppetmaster.
/// In bytecode, these emit a trace marker; actual subscription is handled at runtime.
/// </summary>
public sealed class WatchCompiler : ActionCompilerBase<WatchAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new watch compiler.</summary>
    public WatchCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler;
    }

    /// <inheritdoc/>
    protected override void CompileTyped(WatchAction action, CompilationContext context)
    {
        // Watch actions are runtime operations handled by Puppetmaster.
        // The bytecode VM doesn't execute these directly - the actor runtime
        // intercepts them and forwards to Puppetmaster for subscription management.
        //
        // For bytecode compilation, we emit a trace marker so the action is
        // visible in debug output, but the actual subscription happens at the
        // runtime layer when the tree-walker or actor runtime processes this action.

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
/// Unwatch actions unsubscribe from resource change notifications via Puppetmaster.
/// In bytecode, these emit a trace marker; actual unsubscription is handled at runtime.
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
        // Unwatch actions are runtime operations handled by Puppetmaster.
        // See WatchCompiler for detailed explanation.

        var emitter = context.Emitter;

        // Emit trace marker for debugging
        var traceMsg = $"[unwatch:{action.ResourceType}/{action.ResourceId}]";
        var msgIdx = context.Strings.GetOrAdd(traceMsg);
        emitter.EmitTrace(msgIdx);
    }
}
