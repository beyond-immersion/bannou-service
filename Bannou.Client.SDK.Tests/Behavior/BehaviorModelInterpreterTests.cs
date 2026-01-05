// =============================================================================
// Behavior Model Interpreter Tests
// Tests for the stack-based bytecode virtual machine.
// =============================================================================

using BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.SDK.Tests.Behavior;

/// <summary>
/// Tests for the BehaviorModelInterpreter class.
/// These tests construct models with hand-crafted bytecode to verify
/// interpreter behavior in isolation from the compiler.
/// </summary>
public class BehaviorModelInterpreterTests
{
    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Creates a minimal behavior model with the given bytecode.
    /// </summary>
    private static BehaviorModel CreateModel(
        byte[] bytecode,
        double[]? constants = null,
        string[]? strings = null,
        int inputCount = 0,
        int outputCount = 1)
    {
        var header = new BehaviorModelHeader(
            BehaviorModelHeader.CurrentVersion,
            BehaviorModelFlags.None,
            Guid.NewGuid(),
            0);

        var inputs = new VariableDefinition[inputCount];
        for (var i = 0; i < inputCount; i++)
        {
            inputs[i] = VariableDefinition.Create($"input{i}", i, BehaviorVariableType.Float, 0.0);
        }

        var outputs = new VariableDefinition[outputCount];
        for (var i = 0; i < outputCount; i++)
        {
            outputs[i] = VariableDefinition.Create($"output{i}", i, BehaviorVariableType.Float, 0.0);
        }

        var schema = new StateSchema(inputs, outputs);

        return new BehaviorModel(
            header,
            null,
            schema,
            ContinuationPointTable.Empty,
            constants ?? Array.Empty<double>(),
            strings ?? Array.Empty<string>(),
            bytecode);
    }

    /// <summary>
    /// Writes a 16-bit little-endian value to a byte list.
    /// </summary>
    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
    }

    // =========================================================================
    // BASIC OPERATION TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_PushConstAndSetOutput_SetsOutputValue()
    {
        // Bytecode: PUSH_CONST 0, SET_OUTPUT 0, HALT
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,   // Push constant[0]
            (byte)BehaviorOpcode.SetOutput, 0,   // Store to output[0]
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 42.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void Evaluate_Add_AddsValues()
    {
        // Bytecode: PUSH_CONST 0, PUSH_CONST 1, ADD, SET_OUTPUT 0, HALT
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Add,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 10.0, 25.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(35.0, output[0]);
    }

    [Fact]
    public void Evaluate_Sub_SubtractsValues()
    {
        // Bytecode: PUSH_CONST 0, PUSH_CONST 1, SUB, SET_OUTPUT 0, HALT
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Sub,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 50.0, 20.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(30.0, output[0]);
    }

    [Fact]
    public void Evaluate_Mul_MultipliesValues()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Mul,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 6.0, 7.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void Evaluate_Div_DividesValues()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Div,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 100.0, 4.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(25.0, output[0]);
    }

    [Fact]
    public void Evaluate_DivByZero_ReturnsZero()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Div,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 100.0, 0.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(0.0, output[0]);
    }

    [Fact]
    public void Evaluate_Neg_NegatesValue()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.Neg,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 42.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(-42.0, output[0]);
    }

    // =========================================================================
    // COMPARISON TESTS
    // =========================================================================

    [Theory]
    [InlineData(10.0, 20.0, 1.0)]  // 10 < 20 = true (1.0)
    [InlineData(20.0, 10.0, 0.0)]  // 20 < 10 = false (0.0)
    [InlineData(10.0, 10.0, 0.0)]  // 10 < 10 = false (0.0)
    public void Evaluate_Lt_ComparesCorrectly(double a, double b, double expected)
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Lt,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { a, b };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(expected, output[0]);
    }

    [Theory]
    [InlineData(10.0, 10.0, 1.0)]  // 10 == 10 = true
    [InlineData(10.0, 20.0, 0.0)]  // 10 == 20 = false
    public void Evaluate_Eq_ComparesCorrectly(double a, double b, double expected)
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Eq,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { a, b };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(expected, output[0]);
    }

    // =========================================================================
    // LOGICAL OPERATION TESTS
    // =========================================================================

    [Theory]
    [InlineData(1.0, 0.0)]  // NOT true = false
    [InlineData(0.0, 1.0)]  // NOT false = true
    public void Evaluate_Not_InvertsValue(double input, double expected)
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.Not,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { input };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var inputArray = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(inputArray, output);

        Assert.Equal(expected, output[0]);
    }

    // =========================================================================
    // CONTROL FLOW TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_Jmp_JumpsToTarget()
    {
        // Jump over PUSH_CONST 0, directly to PUSH_CONST 1
        // Layout: Jmp(0) + offset(1-2) + PushConst(3) + idx(4) + SetOutput(5) + idx(6) + PushConst(7)...
        var bytecode = new List<byte>
        {
            (byte)BehaviorOpcode.Jmp  // 0: Jump
        };
        WriteUInt16(bytecode, 7);  // 1-2: Jump target = 7

        // This should be skipped (positions 3-6)
        bytecode.Add((byte)BehaviorOpcode.PushConst);  // 3
        bytecode.Add(0);                                 // 4: constant index 0 (value 100)
        bytecode.Add((byte)BehaviorOpcode.SetOutput);  // 5
        bytecode.Add(0);                                 // 6: output index

        // Jump target (positions 7+)
        bytecode.Add((byte)BehaviorOpcode.PushConst);  // 7
        bytecode.Add(1);                                 // 8: constant index 1 (value 42)
        bytecode.Add((byte)BehaviorOpcode.SetOutput);  // 9
        bytecode.Add(0);                                 // 10: output index
        bytecode.Add((byte)BehaviorOpcode.Halt);       // 11

        var constants = new[] { 100.0, 42.0 };

        var model = CreateModel(bytecode.ToArray(), constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);  // Should get 42, not 100
    }

    [Fact]
    public void Evaluate_JmpIf_JumpsWhenTrue()
    {
        // Layout: PushConst(0) + idx(1) + JmpIf(2) + target(3-4) + PushConst(5) + idx(6) + SetOutput(7) + idx(8) + Halt(9) + PushConst(10)...
        var bytecode = new List<byte>
        {
            (byte)BehaviorOpcode.PushConst, 0,  // 0-1: Push 1.0 (true)
            (byte)BehaviorOpcode.JmpIf           // 2: JmpIf
        };
        WriteUInt16(bytecode, 10);  // 3-4: Jump target = 10

        // This should be skipped (positions 5-9)
        bytecode.Add((byte)BehaviorOpcode.PushConst);  // 5
        bytecode.Add(1);                                 // 6: constant index 1 (value 100)
        bytecode.Add((byte)BehaviorOpcode.SetOutput);  // 7
        bytecode.Add(0);                                 // 8: output index
        bytecode.Add((byte)BehaviorOpcode.Halt);       // 9

        // Jump target (positions 10+)
        bytecode.Add((byte)BehaviorOpcode.PushConst);  // 10
        bytecode.Add(2);                                 // 11: constant index 2 (value 42)
        bytecode.Add((byte)BehaviorOpcode.SetOutput);  // 12
        bytecode.Add(0);                                 // 13: output index
        bytecode.Add((byte)BehaviorOpcode.Halt);       // 14

        var constants = new[] { 1.0, 100.0, 42.0 };

        var model = CreateModel(bytecode.ToArray(), constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);  // Should get 42, not 100
    }

    [Fact]
    public void Evaluate_JmpIf_DoesNotJumpWhenFalse()
    {
        var bytecode = new List<byte>
        {
            (byte)BehaviorOpcode.PushConst, 0,  // Push 0.0 (false)
            (byte)BehaviorOpcode.JmpIf           // JmpIf
        };
        WriteUInt16(bytecode, 15);  // Jump target (won't be used)

        // This should be executed
        bytecode.Add((byte)BehaviorOpcode.PushConst);  // 6
        bytecode.Add(1);                                 // 7: constant index 1 (value 100)
        bytecode.Add((byte)BehaviorOpcode.SetOutput);  // 8
        bytecode.Add(0);                                 // 9
        bytecode.Add((byte)BehaviorOpcode.Halt);       // 10

        var constants = new[] { 0.0, 100.0, 42.0 };

        var model = CreateModel(bytecode.ToArray(), constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(100.0, output[0]);  // Should get 100 (didn't jump)
    }

    // =========================================================================
    // LOCAL VARIABLE TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_StoreAndLoadLocal_WorksCorrectly()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,    // Push 42.0
            (byte)BehaviorOpcode.StoreLocal, 0,   // Store to local[0]
            (byte)BehaviorOpcode.PushLocal, 0,    // Load local[0]
            (byte)BehaviorOpcode.SetOutput, 0,    // Set output[0]
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 42.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);
    }

    // =========================================================================
    // INPUT VARIABLE TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_PushInput_ReadsInputValue()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushInput, 0,    // Push input[0]
            (byte)BehaviorOpcode.SetOutput, 0,    // Set output[0]
            (byte)BehaviorOpcode.Halt
        };

        var model = CreateModel(bytecode, inputCount: 1);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = new[] { 99.0 };
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(99.0, output[0]);
    }

    // =========================================================================
    // SPECIAL OPERATION TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_Lerp_InterpolatesCorrectly()
    {
        // lerp(0, 100, 0.5) = 50
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,    // a = 0
            (byte)BehaviorOpcode.PushConst, 1,    // b = 100
            (byte)BehaviorOpcode.PushConst, 2,    // t = 0.5
            (byte)BehaviorOpcode.Lerp,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 0.0, 100.0, 0.5 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(50.0, output[0]);
    }

    [Fact]
    public void Evaluate_Clamp_ClampsCorrectly()
    {
        // clamp(150, 0, 100) = 100
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,    // value = 150
            (byte)BehaviorOpcode.PushConst, 1,    // min = 0
            (byte)BehaviorOpcode.PushConst, 2,    // max = 100
            (byte)BehaviorOpcode.Clamp,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 150.0, 0.0, 100.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(100.0, output[0]);
    }

    [Fact]
    public void Evaluate_Min_ReturnsMinimum()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Min,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 30.0, 20.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(20.0, output[0]);
    }

    [Fact]
    public void Evaluate_Max_ReturnsMaximum()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.PushConst, 1,
            (byte)BehaviorOpcode.Max,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 30.0, 20.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(30.0, output[0]);
    }

    [Fact]
    public void Evaluate_Abs_ReturnsAbsoluteValue()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.Abs,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { -42.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(42.0, output[0]);
    }

    [Fact]
    public void Evaluate_Floor_RoundsDown()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.Floor,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 3.7 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(3.0, output[0]);
    }

    [Fact]
    public void Evaluate_Ceil_RoundsUp()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,
            (byte)BehaviorOpcode.Ceil,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 3.2 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(4.0, output[0]);
    }

    // =========================================================================
    // DETERMINISTIC RANDOM TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_RandWithSeed_IsDeterministic()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.Rand,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };

        var model = CreateModel(bytecode);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output1 = new double[1];
        var output2 = new double[1];

        // Set same seed and evaluate twice
        interpreter.SetRandomSeed(12345);
        interpreter.Evaluate(input, output1);

        interpreter.SetRandomSeed(12345);
        interpreter.Evaluate(input, output2);

        Assert.Equal(output1[0], output2[0]);
    }

    // =========================================================================
    // STACK OPERATION TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_Dup_DuplicatesTopOfStack()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,    // Push 42
            (byte)BehaviorOpcode.Dup,              // Duplicate
            (byte)BehaviorOpcode.Add,              // Add them together
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 42.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(84.0, output[0]);  // 42 + 42
    }

    [Fact]
    public void Evaluate_Swap_SwapsTopTwoValues()
    {
        var bytecode = new byte[]
        {
            (byte)BehaviorOpcode.PushConst, 0,    // Push 10
            (byte)BehaviorOpcode.PushConst, 1,    // Push 20
            (byte)BehaviorOpcode.Swap,             // Swap: now 10 is on top
            (byte)BehaviorOpcode.Sub,              // 20 - 10 = 10
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
        var constants = new[] { 10.0, 20.0 };

        var model = CreateModel(bytecode, constants);
        var interpreter = new BehaviorModelInterpreter(model);

        var input = Array.Empty<double>();
        var output = new double[1];

        interpreter.Evaluate(input, output);

        Assert.Equal(10.0, output[0]);  // After swap: 20 - 10 = 10
    }
}
