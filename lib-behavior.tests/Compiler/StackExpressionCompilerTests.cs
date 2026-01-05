// =============================================================================
// Stack Expression Compiler Tests
// Tests for compiling expressions to stack-based bytecode.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Compiler.Expressions;
using BeyondImmersion.BannouService.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Behavior.Tests.Compiler;

/// <summary>
/// Tests for the StackExpressionCompiler class.
/// </summary>
public class StackExpressionCompilerTests
{
    // =========================================================================
    // LITERAL TESTS
    // =========================================================================

    [Theory]
    [InlineData("42", BehaviorOpcode.PushConst)]
    [InlineData("3.14", BehaviorOpcode.PushConst)]
    [InlineData("true", BehaviorOpcode.PushConst)]
    [InlineData("false", BehaviorOpcode.PushConst)]
    public void Compile_Literal_GeneratesPushConst(string expression, BehaviorOpcode expectedOpcode)
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile(expression);

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)expectedOpcode, bytecode);
    }

    [Fact]
    public void Compile_StringLiteral_GeneratesPushString()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("'hello'");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.PushString, bytecode);
    }

    // =========================================================================
    // VARIABLE TESTS
    // =========================================================================

    [Fact]
    public void Compile_InputVariable_GeneratesPushInput()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        context.RegisterInput("health", 100.0);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("health");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.PushInput, bytecode);
    }

    [Fact]
    public void Compile_LocalVariable_GeneratesPushLocal()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        context.GetOrAllocateLocal("temp");
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("temp");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.PushLocal, bytecode);
    }

    [Fact]
    public void Compile_UndefinedVariable_AutoRegistersAsInput()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("new_var");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.PushInput, bytecode);
        Assert.True(context.TryGetInput("new_var", out _));
    }

    // =========================================================================
    // ARITHMETIC TESTS
    // =========================================================================

    [Theory]
    [InlineData("1 + 2", BehaviorOpcode.Add)]
    [InlineData("5 - 3", BehaviorOpcode.Sub)]
    [InlineData("4 * 2", BehaviorOpcode.Mul)]
    [InlineData("10 / 2", BehaviorOpcode.Div)]
    [InlineData("7 % 3", BehaviorOpcode.Mod)]
    public void Compile_BinaryArithmetic_GeneratesCorrectOpcode(string expression, BehaviorOpcode expectedOpcode)
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile(expression);

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)expectedOpcode, bytecode);
    }

    [Fact]
    public void Compile_Negation_GeneratesNeg()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("-5");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Neg, bytecode);
    }

    [Fact]
    public void Compile_ComplexArithmetic_GeneratesCorrectOrder()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        // a + b * c should compile as: push a, push b, push c, mul, add
        compiler.Compile("1 + 2 * 3");

        var bytecode = context.Emitter.ToArray();

        // Find positions of opcodes
        var mulPos = Array.IndexOf(bytecode, (byte)BehaviorOpcode.Mul);
        var addPos = Array.IndexOf(bytecode, (byte)BehaviorOpcode.Add);

        // Mul should come before Add (operator precedence)
        Assert.True(mulPos < addPos, "Mul should come before Add due to operator precedence");
    }

    // =========================================================================
    // COMPARISON TESTS
    // =========================================================================

    [Theory]
    [InlineData("1 == 2", BehaviorOpcode.Eq)]
    [InlineData("1 != 2", BehaviorOpcode.Ne)]
    [InlineData("1 < 2", BehaviorOpcode.Lt)]
    [InlineData("1 <= 2", BehaviorOpcode.Le)]
    [InlineData("1 > 2", BehaviorOpcode.Gt)]
    [InlineData("1 >= 2", BehaviorOpcode.Ge)]
    public void Compile_Comparison_GeneratesCorrectOpcode(string expression, BehaviorOpcode expectedOpcode)
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile(expression);

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)expectedOpcode, bytecode);
    }

    // =========================================================================
    // LOGICAL TESTS
    // =========================================================================

    [Fact]
    public void Compile_LogicalNot_GeneratesNot()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("!true");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Not, bytecode);
    }

    [Fact]
    public void Compile_LogicalAnd_GeneratesShortCircuit()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("true && false");

        var bytecode = context.Emitter.ToArray();

        // Short-circuit AND: Dup, JmpUnless, Pop
        Assert.Contains((byte)BehaviorOpcode.Dup, bytecode);
        Assert.Contains((byte)BehaviorOpcode.JmpUnless, bytecode);
        Assert.Contains((byte)BehaviorOpcode.Pop, bytecode);
    }

    [Fact]
    public void Compile_LogicalOr_GeneratesShortCircuit()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("true || false");

        var bytecode = context.Emitter.ToArray();

        // Short-circuit OR: Dup, JmpIf, Pop
        Assert.Contains((byte)BehaviorOpcode.Dup, bytecode);
        Assert.Contains((byte)BehaviorOpcode.JmpIf, bytecode);
        Assert.Contains((byte)BehaviorOpcode.Pop, bytecode);
    }

    // =========================================================================
    // TERNARY TESTS
    // =========================================================================

    [Fact]
    public void Compile_Ternary_GeneratesConditionalJumps()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("true ? 1 : 0");

        var bytecode = context.Emitter.ToArray();

        // Ternary uses JmpUnless for false branch
        Assert.Contains((byte)BehaviorOpcode.JmpUnless, bytecode);
        Assert.Contains((byte)BehaviorOpcode.Jmp, bytecode);
    }

    // =========================================================================
    // FUNCTION CALL TESTS
    // =========================================================================

    [Fact]
    public void Compile_Rand_GeneratesRand()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("rand()");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Rand, bytecode);
    }

    [Fact]
    public void Compile_RandWithArgs_GeneratesRandInt()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("rand(1, 10)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.RandInt, bytecode);
    }

    [Fact]
    public void Compile_Lerp_GeneratesLerp()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("lerp(0, 100, 0.5)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Lerp, bytecode);
    }

    [Fact]
    public void Compile_Clamp_GeneratesClamp()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("clamp(50, 0, 100)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Clamp, bytecode);
    }

    [Fact]
    public void Compile_Abs_GeneratesAbs()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("abs(-5)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Abs, bytecode);
    }

    [Fact]
    public void Compile_Floor_GeneratesFloor()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("floor(3.7)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Floor, bytecode);
    }

    [Fact]
    public void Compile_Ceil_GeneratesCeil()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("ceil(3.2)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Ceil, bytecode);
    }

    [Fact]
    public void Compile_Min_GeneratesMin()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("min(5, 10)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Min, bytecode);
    }

    [Fact]
    public void Compile_Max_GeneratesMax()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("max(5, 10)");

        var bytecode = context.Emitter.ToArray();
        Assert.Contains((byte)BehaviorOpcode.Max, bytecode);
    }

    // =========================================================================
    // NULL COALESCE TESTS
    // =========================================================================

    [Fact]
    public void Compile_NullCoalesce_GeneratesConditional()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        context.RegisterInput("value", 0.0);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("value ?? 10");

        var bytecode = context.Emitter.ToArray();

        // Null coalesce uses Dup, JmpUnless, Pop pattern
        Assert.Contains((byte)BehaviorOpcode.Dup, bytecode);
        Assert.Contains((byte)BehaviorOpcode.JmpUnless, bytecode);
    }

    // =========================================================================
    // ERROR HANDLING TESTS
    // =========================================================================

    [Fact]
    public void Compile_UnknownFunction_AddsError()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("unknown_func(1, 2)");

        Assert.True(context.HasErrors);
        Assert.Contains(context.Errors, e => e.Message.Contains("unknown_func") || e.Message.Contains("Unknown function"));
    }

    [Fact]
    public void Compile_PropertyAccess_AddsError()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        context.RegisterInput("obj", 0.0);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("obj.property");

        Assert.True(context.HasErrors);
        Assert.Contains(context.Errors, e => e.Message.Contains("Property access") || e.Message.Contains("not supported"));
    }

    [Fact]
    public void Compile_IndexAccess_AddsError()
    {
        var context = new CompilationContext(CompilationOptions.Default);
        context.RegisterInput("arr", 0.0);
        var compiler = new StackExpressionCompiler(context);

        compiler.Compile("arr[0]");

        Assert.True(context.HasErrors);
        Assert.Contains(context.Errors, e => e.Message.Contains("Index access") || e.Message.Contains("not supported"));
    }
}
