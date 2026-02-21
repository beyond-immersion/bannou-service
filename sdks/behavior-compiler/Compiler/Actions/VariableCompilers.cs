// =============================================================================
// Variable Action Compilers
// Compilers for variable manipulation actions.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Expressions;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Actions;

/// <summary>
/// Compiles set variable actions.
/// </summary>
public sealed class SetCompiler : ActionCompilerBase<SetAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new set compiler.</summary>
    public SetCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler;
    }

    /// <inheritdoc/>
    protected override void CompileTyped(SetAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Compile the value expression (pushes to stack)
        _exprCompiler.Compile(action.Value);

        // Store to appropriate variable
        if (context.TryGetOutput(action.Variable, out var outputIdx))
        {
            emitter.EmitSetOutput(outputIdx);
        }
        else
        {
            // Treat as local variable
            var localIdx = context.GetOrAllocateLocal(action.Variable);
            emitter.EmitStoreLocal(localIdx);
        }
    }
}

/// <summary>
/// Compiles local variable actions.
/// </summary>
public sealed class LocalCompiler : ActionCompilerBase<LocalAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new local compiler.</summary>
    public LocalCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler;
    }

    /// <inheritdoc/>
    protected override void CompileTyped(LocalAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Compile the value expression
        _exprCompiler.Compile(action.Value);

        // Always store to local scope
        var localIdx = context.GetOrAllocateLocal(action.Variable);
        emitter.EmitStoreLocal(localIdx);
    }
}

/// <summary>
/// Compiles global variable actions.
/// </summary>
public sealed class GlobalCompiler : ActionCompilerBase<GlobalAction>
{
    private readonly StackExpressionCompiler _exprCompiler;

    /// <summary>Creates a new global compiler.</summary>
    public GlobalCompiler(StackExpressionCompiler exprCompiler)
    {
        _exprCompiler = exprCompiler;
    }

    /// <inheritdoc/>
    protected override void CompileTyped(GlobalAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Compile the value expression
        _exprCompiler.Compile(action.Value);

        // In behavior models, "global" means output variable
        // Register as output if not already
        if (!context.TryGetOutput(action.Variable, out var outputIdx))
        {
            outputIdx = context.RegisterOutput(action.Variable);
        }
        emitter.EmitSetOutput(outputIdx);
    }
}

/// <summary>
/// Compiles increment actions.
/// </summary>
public sealed class IncrementCompiler : ActionCompilerBase<IncrementAction>
{
    /// <inheritdoc/>
    protected override void CompileTyped(IncrementAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Load current value
        if (context.TryGetLocal(action.Variable, out var localIdx))
        {
            emitter.EmitPushLocal(localIdx);
        }
        else if (context.TryGetInput(action.Variable, out var inputIdx))
        {
            emitter.EmitPushInput(inputIdx);
            // Can't write back to input, treat as local
            localIdx = context.GetOrAllocateLocal(action.Variable);
        }
        else
        {
            // Create new local initialized to 0
            localIdx = context.GetOrAllocateLocal(action.Variable);
            var zeroIdx = context.Constants.GetOrAdd(0.0);
            emitter.EmitPushConst(zeroIdx);
        }

        // Add increment value
        var byIdx = context.Constants.GetOrAdd(action.By);
        emitter.EmitPushConst(byIdx);
        emitter.Emit(BehaviorOpcode.Add);

        // Store back
        if (context.TryGetOutput(action.Variable, out var outputIdx))
        {
            emitter.EmitSetOutput(outputIdx);
        }
        else
        {
            emitter.EmitStoreLocal(localIdx);
        }
    }
}

/// <summary>
/// Compiles decrement actions.
/// </summary>
public sealed class DecrementCompiler : ActionCompilerBase<DecrementAction>
{
    /// <inheritdoc/>
    protected override void CompileTyped(DecrementAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Load current value
        if (context.TryGetLocal(action.Variable, out var localIdx))
        {
            emitter.EmitPushLocal(localIdx);
        }
        else if (context.TryGetInput(action.Variable, out var inputIdx))
        {
            emitter.EmitPushInput(inputIdx);
            localIdx = context.GetOrAllocateLocal(action.Variable);
        }
        else
        {
            localIdx = context.GetOrAllocateLocal(action.Variable);
            var zeroIdx = context.Constants.GetOrAdd(0.0);
            emitter.EmitPushConst(zeroIdx);
        }

        // Subtract decrement value
        var byIdx = context.Constants.GetOrAdd(action.By);
        emitter.EmitPushConst(byIdx);
        emitter.Emit(BehaviorOpcode.Sub);

        // Store back
        if (context.TryGetOutput(action.Variable, out var outputIdx))
        {
            emitter.EmitSetOutput(outputIdx);
        }
        else
        {
            emitter.EmitStoreLocal(localIdx);
        }
    }
}

/// <summary>
/// Compiles clear (unset) actions.
/// </summary>
public sealed class ClearCompiler : ActionCompilerBase<ClearAction>
{
    /// <inheritdoc/>
    protected override void CompileTyped(ClearAction action, CompilationContext context)
    {
        var emitter = context.Emitter;

        // Set variable to 0
        var zeroIdx = context.Constants.GetOrAdd(0.0);
        emitter.EmitPushConst(zeroIdx);

        if (context.TryGetOutput(action.Variable, out var outputIdx))
        {
            emitter.EmitSetOutput(outputIdx);
        }
        else if (context.TryGetLocal(action.Variable, out var localIdx))
        {
            emitter.EmitStoreLocal(localIdx);
        }
        else
        {
            // Create local and set to 0
            var newLocalIdx = context.GetOrAllocateLocal(action.Variable);
            emitter.EmitStoreLocal(newLocalIdx);
        }
    }
}
