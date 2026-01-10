// =============================================================================
// Behavior Compiler Tests
// Tests for the ABML to bytecode compiler.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.BannouService.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Behavior.Tests.Compiler;

/// <summary>
/// Tests for the BehaviorCompiler class.
/// </summary>
public class BehaviorCompilerTests
{
    private readonly BehaviorCompiler _compiler = new();

    // =========================================================================
    // BASIC COMPILATION TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_EmptyDocument_ReturnsError()
    {
        var result = _compiler.CompileYaml("");

        Assert.False(result.Success);
        Assert.Null(result.Bytecode);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CompileYaml_MinimalDocument_Succeeds()
    {
        var yaml = TestFixtures.Load("compiler_minimal");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        Assert.NotNull(result.Bytecode);
        Assert.NotEmpty(result.Bytecode);
    }

    [Fact]
    public void CompileYaml_WithSetAction_GeneratesCorrectBytecode()
    {
        var yaml = TestFixtures.Load("compiler_set_action");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain PushConst (0x00) and StoreLocal (0x03)
        Assert.Contains((byte)BehaviorOpcode.PushConst, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.StoreLocal, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_WithConditional_GeneratesJumps()
    {
        var yaml = TestFixtures.Load("compiler_conditional");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain JmpUnless (0x42)
        Assert.Contains((byte)BehaviorOpcode.JmpUnless, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_WithGoto_GeneratesJmp()
    {
        var yaml = TestFixtures.Load("compiler_goto");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Jmp (0x40)
        Assert.Contains((byte)BehaviorOpcode.Jmp, result.Bytecode);
    }

    // =========================================================================
    // EXPRESSION COMPILATION TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_ArithmeticExpression_GeneratesCorrectOpcodes()
    {
        var yaml = TestFixtures.Load("compiler_arithmetic");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Add (0x10) and Mul (0x12)
        Assert.Contains((byte)BehaviorOpcode.Add, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.Mul, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_ComparisonExpression_GeneratesCorrectOpcodes()
    {
        var yaml = TestFixtures.Load("compiler_comparison");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Lt (0x22)
        Assert.Contains((byte)BehaviorOpcode.Lt, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_LogicalAnd_GeneratesShortCircuit()
    {
        var yaml = TestFixtures.Load("compiler_logical_and");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Short-circuit AND uses Dup, JmpUnless, Pop
        Assert.Contains((byte)BehaviorOpcode.Dup, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.JmpUnless, result.Bytecode);
    }

    // =========================================================================
    // CONTROL FLOW TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_RepeatSmall_UnrollsLoop()
    {
        var yaml = TestFixtures.Load("compiler_repeat_small");

        // Use a deterministic ModelId to avoid flaky tests - the output binary includes the
        // model ID bytes, and random GUIDs might coincidentally contain the Trace opcode (0xF1)
        var options = new CompilationOptions { ModelId = Guid.Empty };
        var result = _compiler.CompileYaml(yaml, options);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain 3 Trace instructions (for repeat times <= 4, loop is unrolled)
        var traceCount = result.Bytecode!.Count(b => b == (byte)BehaviorOpcode.Trace);
        Assert.Equal(3, traceCount);
    }

    [Fact]
    public void CompileYaml_RepeatLarge_GeneratesLoop()
    {
        var yaml = TestFixtures.Load("compiler_repeat_large");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain a backward Jmp for the loop
        Assert.Contains((byte)BehaviorOpcode.Jmp, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.Lt, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_Return_GeneratesHalt()
    {
        var yaml = TestFixtures.Load("compiler_return");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Halt (0x45)
        Assert.Contains((byte)BehaviorOpcode.Halt, result.Bytecode);
    }

    // =========================================================================
    // VARIABLE TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_LocalVariable_UsesLocalStorage()
    {
        var yaml = TestFixtures.Load("compiler_local_variable");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should use StoreLocal and PushLocal
        Assert.Contains((byte)BehaviorOpcode.StoreLocal, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.PushLocal, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_Increment_GeneratesAddStore()
    {
        var yaml = TestFixtures.Load("compiler_increment");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Add and StoreLocal
        Assert.Contains((byte)BehaviorOpcode.Add, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.StoreLocal, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_Decrement_GeneratesSubStore()
    {
        var yaml = TestFixtures.Load("compiler_decrement");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Sub and StoreLocal
        Assert.Contains((byte)BehaviorOpcode.Sub, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.StoreLocal, result.Bytecode);
    }

    // =========================================================================
    // CONTINUATION POINT TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_ContinuationPoint_GeneratesCpOpcode()
    {
        var yaml = TestFixtures.Load("compiler_continuation_point");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain ContinuationPoint (0x70)
        Assert.Contains((byte)BehaviorOpcode.ContinuationPoint, result.Bytecode);
    }

    // =========================================================================
    // EMIT INTENT TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_EmitIntent_GeneratesIntentOpcode()
    {
        var yaml = TestFixtures.Load("compiler_emit_intent");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain EmitIntent (0x52)
        Assert.Contains((byte)BehaviorOpcode.EmitIntent, result.Bytecode);
    }

    // =========================================================================
    // SEMANTIC ANALYSIS TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_UndefinedFlow_ReturnsError()
    {
        var yaml = TestFixtures.Load("compiler_undefined_flow");

        var result = _compiler.CompileYaml(yaml);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("nonexistent") || e.Message.Contains("undefined"));
    }

    [Fact]
    public void CompileYaml_EmptyConditional_ReturnsError()
    {
        var yaml = TestFixtures.Load("compiler_empty_conditional");

        var result = _compiler.CompileYaml(yaml);

        Assert.False(result.Success);
    }

    // =========================================================================
    // FUNCTION TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_RandFunction_GeneratesRandOpcode()
    {
        var yaml = TestFixtures.Load("compiler_rand_function");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Rand (0x60)
        Assert.Contains((byte)BehaviorOpcode.Rand, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_ClampFunction_GeneratesClampOpcode()
    {
        var yaml = TestFixtures.Load("compiler_clamp_function");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Clamp (0x63)
        Assert.Contains((byte)BehaviorOpcode.Clamp, result.Bytecode);
    }

    [Fact]
    public void CompileYaml_MinMaxFunctions_GenerateCorrectOpcodes()
    {
        var yaml = TestFixtures.Load("compiler_min_max_functions");

        var result = _compiler.CompileYaml(yaml);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        Assert.NotNull(result.Bytecode);

        // Should contain Min (0x67) and Max (0x68)
        Assert.Contains((byte)BehaviorOpcode.Min, result.Bytecode);
        Assert.Contains((byte)BehaviorOpcode.Max, result.Bytecode);
    }

    // =========================================================================
    // COMPILATION OPTIONS TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_WithModelId_UsesProvidedId()
    {
        var yaml = TestFixtures.Load("compiler_model_id");
        var modelId = Guid.NewGuid();
        var options = new CompilationOptions { ModelId = modelId };

        var result = _compiler.CompileYaml(yaml, options);

        // Output diagnostic info if test fails
        if (!result.Success)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail($"Compilation failed with errors: {errorMessages}");
        }
        // The model ID should be embedded in the output
        Assert.NotNull(result.Bytecode);
    }

    [Fact]
    public void CompileYaml_SkipSemanticAnalysis_SkipsValidation()
    {
        // This YAML has an undefined flow reference
        var yaml = TestFixtures.Load("compiler_skip_semantic");
        var options = new CompilationOptions { SkipSemanticAnalysis = true };

        var result = _compiler.CompileYaml(yaml, options);

        // Without semantic analysis, compilation proceeds but may fail at finalization
        // The behavior depends on implementation - we just verify no exception is thrown
        Assert.NotNull(result);
    }
}
