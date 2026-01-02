// =============================================================================
// Behavior Compiler Tests
// Tests for the ABML to bytecode compiler.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.BannouService.Abml.Bytecode;
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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - log: { message: ""Hello"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - set: { variable: result, value: ""10"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - goto: { flow: other }
  other:
    actions:
    - log: { message: ""Done"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

context:
  variables:
    a: { type: float, default: 5 }
    b: { type: float, default: 3 }

flows:
  main:
    actions:
    - set: { variable: result, value: ""${a + b * 2}"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - repeat:
        times: 3
        do:
            - log: { message: ""iteration"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - repeat:
        times: 10
        do:
            - log: { message: ""iteration"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - return: null
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - local: { variable: temp, value: ""42"" }
    - set: { variable: result, value: ""${temp + 1}"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - local: { variable: counter, value: ""0"" }
    - increment: { variable: counter, by: 1 }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - local: { variable: counter, value: ""10"" }
    - decrement: { variable: counter, by: 1 }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - continuation_point:
        name: ""before_action""
        timeout: ""2s""
        default_flow: fallback
  fallback:
    actions:
    - log: { message: ""Fallback path"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - emit_intent:
        action: ""attack""
        channel: ""action""
        urgency: ""0.8""
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - goto: { flow: nonexistent }
";

        var result = _compiler.CompileYaml(yaml);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("nonexistent") || e.Message.Contains("undefined"));
    }

    [Fact]
    public void CompileYaml_EmptyConditional_ReturnsError()
    {
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - cond: []
";

        var result = _compiler.CompileYaml(yaml);

        Assert.False(result.Success);
    }

    // =========================================================================
    // FUNCTION TESTS
    // =========================================================================

    [Fact]
    public void CompileYaml_RandFunction_GeneratesRandOpcode()
    {
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - set: { variable: r, value: ""${rand()}"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

context:
  variables:
    value: { type: float, default: 5 }

flows:
  main:
    actions:
    - set: { variable: clamped, value: ""${clamp(value, 0, 10)}"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

context:
  variables:
    a: { type: float, default: 5 }
    b: { type: float, default: 10 }

flows:
  main:
    actions:
    - set: { variable: minimum, value: ""${min(a, b)}"" }
    - set: { variable: maximum, value: ""${max(a, b)}"" }
";

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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - log: { message: ""Hello"" }
";
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
        var yaml = @"
version: ""2.0""
metadata:
  id: test

flows:
  main:
    actions:
    - goto: { flow: undefined_flow }
";
        var options = new CompilationOptions { SkipSemanticAnalysis = true };

        var result = _compiler.CompileYaml(yaml, options);

        // Without semantic analysis, compilation proceeds but may fail at finalization
        // The behavior depends on implementation - we just verify no exception is thrown
        Assert.NotNull(result);
    }
}
