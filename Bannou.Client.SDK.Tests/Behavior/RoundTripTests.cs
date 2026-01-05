// =============================================================================
// Round-Trip Tests
// Tests that compile YAML → serialize → deserialize → execute correctly.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.SDK.Tests.Behavior;

/// <summary>
/// Round-trip tests verifying the full pipeline:
/// YAML → BehaviorCompiler → binary → BehaviorModel.Deserialize → BehaviorModelInterpreter → output.
/// </summary>
public class RoundTripTests
{
    // =========================================================================
    // SIMPLE ARITHMETIC
    // =========================================================================

    [Fact]
    public void RoundTrip_SimpleSet_ProducesCorrectOutput()
    {
        // Use 'global:' to explicitly set output variables (set: treats unknowns as locals)
        const string yaml = @"
version: '2.0'
metadata:
  id: simple-set
flows:
  main:
    actions:
    - global: { variable: result, value: '42' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Addition_ProducesCorrectOutput()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: addition-test
flows:
  main:
    actions:
    - global: { variable: result, value: '10 + 32' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_ComplexArithmetic_ProducesCorrectOutput()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: complex-arithmetic
flows:
  main:
    actions:
    - global: { variable: result, value: '(10 + 5) * 2 - 8 / 2' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        // (10 + 5) * 2 - 8 / 2 = 15 * 2 - 4 = 30 - 4 = 26
        Assert.Equal(26.0, output[0]);
    }

    // =========================================================================
    // INPUT VARIABLES
    // =========================================================================

    [Fact]
    public void RoundTrip_InputVariable_ReadsFromInput()
    {
        // Variables in context.variables are registered as inputs
        const string yaml = @"
version: '2.0'
metadata:
  id: input-test
context:
  variables:
    health: { default: 100 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'health * 2' }
";

        var output = CompileAndExecute(yaml, inputs: new[] { 50.0 }, outputCount: 1);

        Assert.Equal(100.0, output[0]);  // 50 * 2 = 100
    }

    [Fact]
    public void RoundTrip_MultipleInputs_AllAccessible()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: multi-input-test
context:
  variables:
    a: { default: 0 }
    b: { default: 0 }
    c: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'a + b * c' }
";

        var output = CompileAndExecute(yaml, inputs: new[] { 10.0, 5.0, 3.0 }, outputCount: 1);

        // 10 + 5 * 3 = 10 + 15 = 25
        Assert.Equal(25.0, output[0]);
    }

    // =========================================================================
    // COMPARISONS AND CONDITIONALS
    // =========================================================================

    [Fact]
    public void RoundTrip_Comparison_LessThan()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: comparison-test
context:
  variables:
    x: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'x < 50 ? 1 : 0' }
";

        var outputTrue = CompileAndExecute(yaml, inputs: new[] { 30.0 }, outputCount: 1);
        Assert.Equal(1.0, outputTrue[0]);  // 30 < 50 is true

        var outputFalse = CompileAndExecute(yaml, inputs: new[] { 70.0 }, outputCount: 1);
        Assert.Equal(0.0, outputFalse[0]);  // 70 < 50 is false
    }

    [Fact]
    public void RoundTrip_TernaryOperator_ChoosesBranch()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: ternary-test
context:
  variables:
    value: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'value > 0 ? 100 : -100' }
";

        var positive = CompileAndExecute(yaml, inputs: new[] { 5.0 }, outputCount: 1);
        Assert.Equal(100.0, positive[0]);

        var negative = CompileAndExecute(yaml, inputs: new[] { -5.0 }, outputCount: 1);
        Assert.Equal(-100.0, negative[0]);
    }

    // =========================================================================
    // LOCAL VARIABLES
    // =========================================================================

    [Fact]
    public void RoundTrip_LocalVariable_StoresAndReads()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: local-var-test
flows:
  main:
    actions:
    - local: { variable: temp, value: '21' }
    - global: { variable: result, value: 'temp * 2' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_MultipleLocals_IndependentStorage()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: multi-local-test
flows:
  main:
    actions:
    - local: { variable: a, value: '10' }
    - local: { variable: b, value: '20' }
    - local: { variable: c, value: '12' }
    - global: { variable: result, value: 'a + b + c' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);  // 10 + 20 + 12 = 42
    }

    // =========================================================================
    // BUILT-IN FUNCTIONS
    // =========================================================================

    [Fact]
    public void RoundTrip_Abs_NegativeToPositive()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: abs-test
flows:
  main:
    actions:
    - global: { variable: result, value: 'abs(-42)' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Clamp_BoundsValue()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: clamp-test
context:
  variables:
    value: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'clamp(value, 0, 100)' }
";

        var low = CompileAndExecute(yaml, inputs: new[] { -50.0 }, outputCount: 1);
        Assert.Equal(0.0, low[0]);

        var mid = CompileAndExecute(yaml, inputs: new[] { 50.0 }, outputCount: 1);
        Assert.Equal(50.0, mid[0]);

        var high = CompileAndExecute(yaml, inputs: new[] { 150.0 }, outputCount: 1);
        Assert.Equal(100.0, high[0]);
    }

    [Fact]
    public void RoundTrip_MinMax_SelectsExtreme()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: minmax-test
flows:
  main:
    actions:
    - global: { variable: minResult, value: 'min(30, 50)' }
    - global: { variable: maxResult, value: 'max(30, 50)' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 2);

        Assert.Equal(30.0, output[0]);  // min
        Assert.Equal(50.0, output[1]);  // max
    }

    [Fact]
    public void RoundTrip_Lerp_Interpolates()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: lerp-test
flows:
  main:
    actions:
    - global: { variable: result, value: 'lerp(0, 100, 0.5)' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(50.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Floor_RoundsDown()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: floor-test
flows:
  main:
    actions:
    - global: { variable: result, value: 'floor(42.9)' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Ceil_RoundsUp()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: ceil-test
flows:
  main:
    actions:
    - global: { variable: result, value: 'ceil(42.1)' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(43.0, output[0]);
    }

    // =========================================================================
    // LOGICAL OPERATORS
    // =========================================================================

    [Fact]
    public void RoundTrip_LogicalAnd_ShortCircuits()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: logical-and-test
context:
  variables:
    a: { default: 0 }
    b: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'a > 0 && b > 0 ? 1 : 0' }
";

        var bothTrue = CompileAndExecute(yaml, inputs: new[] { 5.0, 10.0 }, outputCount: 1);
        Assert.Equal(1.0, bothTrue[0]);

        var firstFalse = CompileAndExecute(yaml, inputs: new[] { -5.0, 10.0 }, outputCount: 1);
        Assert.Equal(0.0, firstFalse[0]);

        var secondFalse = CompileAndExecute(yaml, inputs: new[] { 5.0, -10.0 }, outputCount: 1);
        Assert.Equal(0.0, secondFalse[0]);
    }

    [Fact]
    public void RoundTrip_LogicalOr_ShortCircuits()
    {
        const string yaml = @"
version: '2.0'
metadata:
  id: logical-or-test
context:
  variables:
    a: { default: 0 }
    b: { default: 0 }
flows:
  main:
    actions:
    - global: { variable: result, value: 'a > 0 || b > 0 ? 1 : 0' }
";

        var bothFalse = CompileAndExecute(yaml, inputs: new[] { -5.0, -10.0 }, outputCount: 1);
        Assert.Equal(0.0, bothFalse[0]);

        var firstTrue = CompileAndExecute(yaml, inputs: new[] { 5.0, -10.0 }, outputCount: 1);
        Assert.Equal(1.0, firstTrue[0]);

        var secondTrue = CompileAndExecute(yaml, inputs: new[] { -5.0, 10.0 }, outputCount: 1);
        Assert.Equal(1.0, secondTrue[0]);
    }

    // =========================================================================
    // INCREMENT/DECREMENT
    // =========================================================================

    [Fact]
    public void RoundTrip_Increment_AddsOne()
    {
        // Use local for counter since outputs are write-only (no PushOutput opcode)
        const string yaml = @"
version: '2.0'
metadata:
  id: increment-test
flows:
  main:
    actions:
    - local: { variable: counter, value: '10' }
    - increment: { variable: counter }
    - increment: { variable: counter }
    - increment: { variable: counter }
    - global: { variable: result, value: 'counter' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(13.0, output[0]);  // 10 + 1 + 1 + 1 = 13
    }

    [Fact]
    public void RoundTrip_Decrement_SubtractsOne()
    {
        // Use local for counter since outputs are write-only
        const string yaml = @"
version: '2.0'
metadata:
  id: decrement-test
flows:
  main:
    actions:
    - local: { variable: counter, value: '10' }
    - decrement: { variable: counter }
    - decrement: { variable: counter }
    - global: { variable: result, value: 'counter' }
";

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(8.0, output[0]);  // 10 - 1 - 1 = 8
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private static double[] CompileAndExecute(
        string yaml,
        double[]? inputs = null,
        int inputCount = 0,
        int outputCount = 1)
    {
        // Step 1: Compile YAML to binary
        var compiler = new BehaviorCompiler();
        var result = compiler.CompileYaml(yaml);

        if (!result.Success)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Compilation failed: {errors}");
        }

        if (result.Bytecode == null)
        {
            throw new InvalidOperationException("Compilation succeeded but bytecode is null");
        }

        // Step 2: Deserialize binary to BehaviorModel
        var model = BehaviorModel.Deserialize(result.Bytecode);

        // Step 3: Create interpreter and execute
        var interpreter = new BehaviorModelInterpreter(model);

        var inputState = inputs ?? new double[inputCount];
        var outputState = new double[outputCount];

        interpreter.Evaluate(inputState, outputState);

        return outputState;
    }
}
