// =============================================================================
// Round-Trip Tests
// Tests that compile YAML -> serialize -> deserialize -> execute correctly.
//
// These tests live in client.integration.tests because they require both:
// - BehaviorCompiler from lib-behavior (server-side compiler)
// - BehaviorModel/BehaviorModelInterpreter from client SDK (client-side runtime)
//
// Using aliases disambiguate the namespace collision between server and client
// runtime classes that share identical names.
// =============================================================================

using Compiler = BeyondImmersion.Bannou.Behavior.Compiler;
using ClientRuntime = BeyondImmersion.Bannou.Client.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Integration.Tests.Behavior;

/// <summary>
/// Round-trip tests verifying the full pipeline:
/// YAML -> BehaviorCompiler -> binary -> BehaviorModel.Deserialize -> BehaviorModelInterpreter -> output.
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
        var yaml = TestFixtures.Load("roundtrip_simple_set");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Addition_ProducesCorrectOutput()
    {
        var yaml = TestFixtures.Load("roundtrip_addition");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_ComplexArithmetic_ProducesCorrectOutput()
    {
        var yaml = TestFixtures.Load("roundtrip_complex_arithmetic");

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
        var yaml = TestFixtures.Load("roundtrip_input_variable");

        var output = CompileAndExecute(yaml, inputs: [50.0], outputCount: 1);

        Assert.Equal(100.0, output[0]);  // 50 * 2 = 100
    }

    [Fact]
    public void RoundTrip_MultipleInputs_AllAccessible()
    {
        var yaml = TestFixtures.Load("roundtrip_multiple_inputs");

        var output = CompileAndExecute(yaml, inputs: [10.0, 5.0, 3.0], outputCount: 1);

        // 10 + 5 * 3 = 10 + 15 = 25
        Assert.Equal(25.0, output[0]);
    }

    // =========================================================================
    // COMPARISONS AND CONDITIONALS
    // =========================================================================

    [Fact]
    public void RoundTrip_Comparison_LessThan()
    {
        var yaml = TestFixtures.Load("roundtrip_comparison");

        var outputTrue = CompileAndExecute(yaml, inputs: [30.0], outputCount: 1);
        Assert.Equal(1.0, outputTrue[0]);  // 30 < 50 is true

        var outputFalse = CompileAndExecute(yaml, inputs: [70.0], outputCount: 1);
        Assert.Equal(0.0, outputFalse[0]);  // 70 < 50 is false
    }

    [Fact]
    public void RoundTrip_TernaryOperator_ChoosesBranch()
    {
        var yaml = TestFixtures.Load("roundtrip_ternary");

        var positive = CompileAndExecute(yaml, inputs: [5.0], outputCount: 1);
        Assert.Equal(100.0, positive[0]);

        var negative = CompileAndExecute(yaml, inputs: [-5.0], outputCount: 1);
        Assert.Equal(-100.0, negative[0]);
    }

    // =========================================================================
    // LOCAL VARIABLES
    // =========================================================================

    [Fact]
    public void RoundTrip_LocalVariable_StoresAndReads()
    {
        var yaml = TestFixtures.Load("roundtrip_local_variable");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_MultipleLocals_IndependentStorage()
    {
        var yaml = TestFixtures.Load("roundtrip_multiple_locals");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);  // 10 + 20 + 12 = 42
    }

    // =========================================================================
    // BUILT-IN FUNCTIONS
    // =========================================================================

    [Fact]
    public void RoundTrip_Abs_NegativeToPositive()
    {
        var yaml = TestFixtures.Load("roundtrip_abs");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Clamp_BoundsValue()
    {
        var yaml = TestFixtures.Load("roundtrip_clamp");

        var low = CompileAndExecute(yaml, inputs: [-50.0], outputCount: 1);
        Assert.Equal(0.0, low[0]);

        var mid = CompileAndExecute(yaml, inputs: [50.0], outputCount: 1);
        Assert.Equal(50.0, mid[0]);

        var high = CompileAndExecute(yaml, inputs: [150.0], outputCount: 1);
        Assert.Equal(100.0, high[0]);
    }

    [Fact]
    public void RoundTrip_MinMax_SelectsExtreme()
    {
        var yaml = TestFixtures.Load("roundtrip_minmax");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 2);

        Assert.Equal(30.0, output[0]);  // min
        Assert.Equal(50.0, output[1]);  // max
    }

    [Fact]
    public void RoundTrip_Lerp_Interpolates()
    {
        var yaml = TestFixtures.Load("roundtrip_lerp");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(50.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Floor_RoundsDown()
    {
        var yaml = TestFixtures.Load("roundtrip_floor");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void RoundTrip_Ceil_RoundsUp()
    {
        var yaml = TestFixtures.Load("roundtrip_ceil");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(43.0, output[0]);
    }

    // =========================================================================
    // LOGICAL OPERATORS
    // =========================================================================

    [Fact]
    public void RoundTrip_LogicalAnd_ShortCircuits()
    {
        var yaml = TestFixtures.Load("roundtrip_logical_and");

        var bothTrue = CompileAndExecute(yaml, inputs: [5.0, 10.0], outputCount: 1);
        Assert.Equal(1.0, bothTrue[0]);

        var firstFalse = CompileAndExecute(yaml, inputs: [-5.0, 10.0], outputCount: 1);
        Assert.Equal(0.0, firstFalse[0]);

        var secondFalse = CompileAndExecute(yaml, inputs: [5.0, -10.0], outputCount: 1);
        Assert.Equal(0.0, secondFalse[0]);
    }

    [Fact]
    public void RoundTrip_LogicalOr_ShortCircuits()
    {
        var yaml = TestFixtures.Load("roundtrip_logical_or");

        var bothFalse = CompileAndExecute(yaml, inputs: [-5.0, -10.0], outputCount: 1);
        Assert.Equal(0.0, bothFalse[0]);

        var firstTrue = CompileAndExecute(yaml, inputs: [5.0, -10.0], outputCount: 1);
        Assert.Equal(1.0, firstTrue[0]);

        var secondTrue = CompileAndExecute(yaml, inputs: [-5.0, 10.0], outputCount: 1);
        Assert.Equal(1.0, secondTrue[0]);
    }

    // =========================================================================
    // INCREMENT/DECREMENT
    // =========================================================================

    [Fact]
    public void RoundTrip_Increment_AddsOne()
    {
        // Use local for counter since outputs are write-only (no PushOutput opcode)
        var yaml = TestFixtures.Load("roundtrip_increment");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(13.0, output[0]);  // 10 + 1 + 1 + 1 = 13
    }

    [Fact]
    public void RoundTrip_Decrement_SubtractsOne()
    {
        // Use local for counter since outputs are write-only
        var yaml = TestFixtures.Load("roundtrip_decrement");

        var output = CompileAndExecute(yaml, inputCount: 0, outputCount: 1);

        Assert.Equal(8.0, output[0]);  // 10 - 1 - 1 = 8
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Compiles YAML using the server-side compiler and executes it using
    /// the client-side interpreter, verifying full round-trip compatibility.
    /// </summary>
    private static double[] CompileAndExecute(
        string yaml,
        double[]? inputs = null,
        int inputCount = 0,
        int outputCount = 1)
    {
        // Step 1: Compile YAML to binary using server-side compiler
        var compiler = new Compiler.BehaviorCompiler();
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

        // Step 2: Deserialize binary using CLIENT-SIDE BehaviorModel
        // This verifies the client SDK can read compiler output
        var model = ClientRuntime.BehaviorModel.Deserialize(result.Bytecode);

        // Step 3: Execute using CLIENT-SIDE interpreter
        // This verifies the client SDK can execute the behavior
        var interpreter = new ClientRuntime.BehaviorModelInterpreter(model);

        var inputState = inputs ?? new double[inputCount];
        var outputState = new double[outputCount];

        interpreter.Evaluate(inputState, outputState);

        return outputState;
    }
}
