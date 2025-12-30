// =============================================================================
// Output Action Compilers
// Compilers for output, logging, and domain-specific actions.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler.Expressions;
using BeyondImmersion.BannouService.Abml.Bytecode;
using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.Bannou.Behavior.Compiler.Actions;

/// <summary>
/// Compiles log actions.
/// </summary>
public sealed class LogCompiler : ActionCompilerBase<LogAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new log compiler.</summary>
    public LogCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(LogAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Add message to string table
        var strIdx = context.Strings.GetOrAdd(action.Message);

        // Emit trace instruction (debug only)
        emitter.EmitTrace(strIdx);
    }
}

/// <summary>
/// Compiles emit_intent actions for behavior output.
/// </summary>
public sealed class EmitIntentCompiler : ActionCompilerBase<EmitIntentAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new emit intent compiler.</summary>
    public EmitIntentCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(EmitIntentAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Map channel name to enum
        var channel = action.Channel.ToLowerInvariant() switch
        {
            "action" => IntentChannel.Action,
            "locomotion" => IntentChannel.Locomotion,
            "attention" => IntentChannel.Attention,
            "stance" => IntentChannel.Stance,
            _ => IntentChannel.Action
        };

        // Push action name as string index
        var actionStrIdx = context.Strings.GetOrAdd(action.Action);
        emitter.EmitPushString(actionStrIdx);

        // Push urgency
        _exprCompiler.Compile(action.Urgency);

        // Emit intent (pops action_idx and urgency)
        emitter.EmitEmitIntent((byte)channel);
    }
}

/// <summary>
/// Compiles continuation_point actions for streaming composition.
/// </summary>
public sealed class ContinuationPointCompiler : ActionCompilerBase<ContinuationPointAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new continuation point compiler.</summary>
    public ContinuationPointCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(ContinuationPointAction action, CompilationContext context)
    {
        var emitter = context.Emitter;
        var labels = context.Labels;

        // Parse timeout (e.g., "2s", "500ms")
        var timeoutMs = ParseTimeout(action.Timeout);

        // Record current bytecode offset
        var cpOffset = (uint)emitter.CurrentOffset;

        // Register continuation point (default flow offset will be set later)
        var cpIndex = context.RegisterContinuationPoint(action.Name, timeoutMs, cpOffset);

        // Add name to string table for debugging
        context.Strings.GetOrAdd(action.Name);

        // Emit continuation point opcode
        emitter.EmitContinuationPoint(cpIndex);

        // The default flow label
        var defaultFlowLabel = labels.GetOrAllocateFlowLabel(action.DefaultFlow);

        // Record where the default flow should jump to
        // This will be patched when the flow is compiled
        context.SetContinuationPointDefaultFlow(cpIndex, (uint)emitter.CurrentOffset);

        // Jump to default flow
        emitter.EmitJmp(defaultFlowLabel);
    }

    private static uint ParseTimeout(string timeout)
    {
        if (string.IsNullOrEmpty(timeout))
        {
            return 0;
        }

        timeout = timeout.Trim().ToLowerInvariant();

        if (timeout.EndsWith("ms"))
        {
            if (uint.TryParse(timeout[..^2], out var ms))
            {
                return ms;
            }
        }
        else if (timeout.EndsWith('s'))
        {
            if (double.TryParse(timeout[..^1], out var seconds))
            {
                return (uint)(seconds * 1000);
            }
        }
        else if (uint.TryParse(timeout, out var defaultMs))
        {
            return defaultMs;
        }

        return 0;
    }
}

/// <summary>
/// Compiles domain-specific actions (catch-all for unknown action types).
/// </summary>
public sealed class DomainActionCompiler : ActionCompilerBase<DomainAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new domain action compiler.</summary>
    public DomainActionCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(DomainAction action, CompilationContext context)
    {
        // Domain actions are application-specific and not directly executable in bytecode
        // They would need to be either:
        // 1. Transformed to service calls (not in this simple VM)
        // 2. Represented as output values that the runtime interprets
        // 3. Ignored for behavior compilation

        // For now, we treat known domain actions as output signals

        switch (action.Name.ToLowerInvariant())
        {
            case "animate":
            case "speak":
            case "move_to":
            case "look_at":
            case "wait":
                // These are cinematic actions - emit as trace for now
                var msgIdx = context.Strings.GetOrAdd($"[{action.Name}]");
                context.Emitter.EmitTrace(msgIdx);
                break;

            default:
                // Unknown domain action - log warning
                context.AddError($"Domain action '{action.Name}' cannot be compiled to behavior bytecode. Consider using emit_intent instead.");
                break;
        }
    }
}
