// =============================================================================
// Control Flow Action Compilers
// Compilers for conditional, loop, and flow control actions.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler.Expressions;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Behavior.Runtime;

namespace BeyondImmersion.Bannou.Behavior.Compiler.Actions;

/// <summary>
/// Compiles conditional (if/else-if/else) actions.
/// </summary>
public sealed class CondCompiler : ActionCompilerBase<CondAction>
{
    private readonly ActionCompilerRegistry _registry;
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new conditional compiler.</summary>
    public CondCompiler(ActionCompilerRegistry registry, StackExpressionCompiler exprCompiler)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(CondAction action, CompilationContext context)
    {
        var emitter = context.Emitter;
        var labels = context.Labels;

        var endLabel = labels.AllocateLabel();
        var branchLabels = new int[action.Branches.Count];

        // Allocate labels for each branch
        for (var i = 0; i < action.Branches.Count; i++)
        {
            branchLabels[i] = labels.AllocateLabel();
        }

        var elseLabel = labels.AllocateLabel();

        // Compile each branch
        for (var i = 0; i < action.Branches.Count; i++)
        {
            var branch = action.Branches[i];
            var nextLabel = i + 1 < action.Branches.Count ? branchLabels[i + 1] : elseLabel;

            // Compile condition
            _exprCompiler.Compile(branch.When);

            // Jump to next branch if condition is false
            emitter.EmitJmpUnless(nextLabel);

            // Compile then actions
            _registry.CompileActions(branch.Then, context);

            // Jump to end
            emitter.EmitJmp(endLabel);

            // Define next branch label
            if (i + 1 < action.Branches.Count)
            {
                emitter.DefineLabel(branchLabels[i + 1]);
            }
        }

        // Else branch
        emitter.DefineLabel(elseLabel);
        if (action.ElseBranch != null)
        {
            _registry.CompileActions(action.ElseBranch, context);
        }

        // End
        emitter.DefineLabel(endLabel);
    }
}

/// <summary>
/// Compiles for-each loop actions.
/// </summary>
public sealed class ForEachCompiler : ActionCompilerBase<ForEachAction>
{
    private readonly ActionCompilerRegistry _registry;
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new for-each compiler.</summary>
    public ForEachCompiler(ActionCompilerRegistry registry, StackExpressionCompiler exprCompiler)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(ForEachAction action, CompilationContext context)
    {
        // For-each over collections is not directly supported in the simple numeric VM
        // For behavior models, we typically don't iterate over collections
        context.AddError($"for_each is not supported in behavior bytecode. Consider unrolling or using repeat.");
    }
}

/// <summary>
/// Compiles repeat (bounded loop) actions.
/// </summary>
public sealed class RepeatCompiler : ActionCompilerBase<RepeatAction>
{
    private readonly ActionCompilerRegistry _registry;

    /// <summary>Creates a new repeat compiler.</summary>
    public RepeatCompiler(ActionCompilerRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(RepeatAction action, CompilationContext context)
    {
        var emitter = context.Emitter;
        var labels = context.Labels;

        // For small repeat counts, unroll the loop
        if (action.Times <= 4)
        {
            for (var i = 0; i < action.Times; i++)
            {
                _registry.CompileActions(action.Do, context);
            }
            return;
        }

        // For larger counts, use a loop counter
        var counterLocal = context.GetOrAllocateLocal($"__repeat_{labels.AllocateLabel()}");
        var loopStart = labels.AllocateLabel();
        var loopEnd = labels.AllocateLabel();

        // Initialize counter to 0
        var zeroIdx = context.Constants.GetOrAdd(0.0);
        emitter.EmitPushConst(zeroIdx);
        emitter.EmitStoreLocal(counterLocal);

        // Loop start
        emitter.DefineLabel(loopStart);

        // Check if counter < times
        emitter.EmitPushLocal(counterLocal);
        var timesIdx = context.Constants.GetOrAdd(action.Times);
        emitter.EmitPushConst(timesIdx);
        emitter.Emit(BehaviorOpcode.Lt);
        emitter.EmitJmpUnless(loopEnd);

        // Execute body
        _registry.CompileActions(action.Do, context);

        // Increment counter
        emitter.EmitPushLocal(counterLocal);
        var oneIdx = context.Constants.GetOrAdd(1.0);
        emitter.EmitPushConst(oneIdx);
        emitter.Emit(BehaviorOpcode.Add);
        emitter.EmitStoreLocal(counterLocal);

        // Jump back to start
        emitter.EmitJmp(loopStart);

        // Loop end
        emitter.DefineLabel(loopEnd);
    }
}

/// <summary>
/// Compiles goto (tail call) actions.
/// </summary>
public sealed class GotoCompiler : ActionCompilerBase<GotoAction>
{
    /// <inheritdoc/>
    protected override void CompileTyped(GotoAction action, CompilationContext context)
    {
        var emitter = context.Emitter;
        var labels = context.Labels;

        // Get or allocate label for target flow
        var flowLabel = labels.GetOrAllocateFlowLabel(action.Flow);

        // Jump to flow (tail call - no return)
        emitter.EmitJmp(flowLabel);
    }
}

/// <summary>
/// Compiles call (subroutine) actions.
/// </summary>
public sealed class CallCompiler : ActionCompilerBase<CallAction>
{
    /// <inheritdoc/>
    protected override void CompileTyped(CallAction action, CompilationContext context)
    {
        // Call is similar to goto but with return
        // For now, we treat it as a jump since our simple VM doesn't have a call stack
        context.AddError("call is not fully supported in behavior bytecode. Use goto for now.");

        var emitter = context.Emitter;
        var labels = context.Labels;
        var flowLabel = labels.GetOrAllocateFlowLabel(action.Flow);
        emitter.EmitJmp(flowLabel);
    }
}

/// <summary>
/// Compiles return actions.
/// </summary>
public sealed class ReturnCompiler : ActionCompilerBase<ReturnAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new return compiler.</summary>
    public ReturnCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler ?? throw new ArgumentNullException(nameof(exprCompiler));
    }

    /// <inheritdoc/>
    protected override void CompileTyped(ReturnAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // If there's a return value, compile it and set as output 0
        if (!string.IsNullOrEmpty(action.Value))
        {
            _exprCompiler.Compile(action.Value);
            emitter.EmitSetOutput(0);
        }

        // Halt execution
        emitter.Emit(BehaviorOpcode.Halt);
    }
}
