// =============================================================================
// Cinematic Interpreter Tests
// Tests for streaming composition and continuation point handling.
// =============================================================================

using BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.SDK.Tests.Behavior;

/// <summary>
/// Tests for the CinematicInterpreter class.
/// These tests verify continuation point handling and extension registration.
/// </summary>
public class CinematicInterpreterTests
{
    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Creates a minimal behavior model with the given bytecode and optional continuation points.
    /// </summary>
    private static BehaviorModel CreateModel(
        byte[] bytecode,
        double[]? constants = null,
        string[]? strings = null,
        int inputCount = 1,
        int outputCount = 1,
        ContinuationPoint[]? continuationPoints = null)
    {
        var flags = continuationPoints?.Length > 0
            ? BehaviorModelFlags.HasContinuationPoints
            : BehaviorModelFlags.None;

        var header = new BehaviorModelHeader(
            BehaviorModelHeader.CurrentVersion,
            flags,
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
        var cpTable = continuationPoints?.Length > 0
            ? new ContinuationPointTable(continuationPoints)
            : ContinuationPointTable.Empty;

        return new BehaviorModel(
            header,
            null,
            schema,
            cpTable,
            constants ?? Array.Empty<double>(),
            strings ?? Array.Empty<string>(),
            bytecode);
    }

    /// <summary>
    /// Creates simple bytecode that sets output[0] to a constant value.
    /// </summary>
    private static byte[] CreateSimpleBytecode(byte constIndex = 0)
    {
        return new byte[]
        {
            (byte)BehaviorOpcode.PushConst, constIndex,
            (byte)BehaviorOpcode.SetOutput, 0,
            (byte)BehaviorOpcode.Halt
        };
    }

    // =========================================================================
    // BASIC FUNCTIONALITY TESTS
    // =========================================================================

    [Fact]
    public void Constructor_WithValidModel_CreatesInterpreter()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 42.0 });

        var interpreter = new CinematicInterpreter(model);

        Assert.NotNull(interpreter);
        Assert.False(interpreter.HasActiveExtension);
        Assert.Null(interpreter.ActiveContinuationPoint);
    }

    [Fact]
    public void Evaluate_BasicModel_ProducesCorrectOutput()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 42.0 });
        var interpreter = new CinematicInterpreter(model);

        var result = interpreter.Evaluate();

        Assert.False(result.IsAtContinuationPoint);
        Assert.Equal(42.0, interpreter.OutputState[0]);
    }

    [Fact]
    public void SetInput_ByName_UpdatesInputState()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 0.0 });
        var interpreter = new CinematicInterpreter(model);

        interpreter.SetInput("input0", 100.0);

        Assert.Equal(100.0, interpreter.InputState[0]);
    }

    [Fact]
    public void GetOutput_ByName_ReturnsOutputValue()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 42.0 });
        var interpreter = new CinematicInterpreter(model);

        interpreter.Evaluate();
        var output = interpreter.GetOutput("output0");

        Assert.Equal(42.0, output);
    }

    [Fact]
    public void GetOutput_UnknownName_ReturnsZero()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 42.0 });
        var interpreter = new CinematicInterpreter(model);

        interpreter.Evaluate();
        var output = interpreter.GetOutput("unknown");

        Assert.Equal(0.0, output);
    }

    // =========================================================================
    // EXTENSION REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void RegisterExtension_ValidContinuationPoint_RegistersExtension()
    {
        var baseModel = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 10.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("before_resolution", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "before_resolution" });

        var extensionModel = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 99.0 });

        var interpreter = new CinematicInterpreter(baseModel);

        interpreter.RegisterExtension("before_resolution", extensionModel);

        // Extension is registered but not active until continuation point is hit
        Assert.False(interpreter.HasActiveExtension);
    }

    [Fact]
    public void RemoveExtension_RegisteredExtension_ReturnsTrue()
    {
        var baseModel = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 10.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var extensionModel = CreateModel(CreateSimpleBytecode(), constants: new[] { 99.0 });
        var interpreter = new CinematicInterpreter(baseModel);

        interpreter.RegisterExtension("test_point", extensionModel);
        var removed = interpreter.RemoveExtension("test_point");

        Assert.True(removed);
    }

    [Fact]
    public void RemoveExtension_UnregisteredExtension_ReturnsFalse()
    {
        var model = CreateModel(CreateSimpleBytecode(), constants: new[] { 42.0 });
        var interpreter = new CinematicInterpreter(model);

        var removed = interpreter.RemoveExtension("nonexistent");

        Assert.False(removed);
    }

    // =========================================================================
    // CONTINUATION POINT ACTIVATION TESTS
    // =========================================================================

    [Fact]
    public void ActivateContinuationPoint_ValidPoint_SetsActiveState()
    {
        var model = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 42.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var interpreter = new CinematicInterpreter(model);

        interpreter.ActivateContinuationPoint("test_point");

        Assert.True(interpreter.HasActiveExtension);
        Assert.Equal("test_point", interpreter.ActiveContinuationPoint);
    }

    [Fact]
    public void ActivateContinuationPoint_WithExtension_ExecutesExtension()
    {
        var baseModel = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 10.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var extensionModel = CreateModel(CreateSimpleBytecode(), constants: new[] { 99.0 });
        var interpreter = new CinematicInterpreter(baseModel);

        interpreter.RegisterExtension("test_point", extensionModel);
        interpreter.ActivateContinuationPoint("test_point");

        var result = interpreter.Evaluate();

        Assert.True(result.IsAtContinuationPoint);
        Assert.Equal("test_point", result.ContinuationPointName);
        Assert.Equal(99.0, interpreter.OutputState[0]); // Extension's output
    }

    [Fact]
    public void CompleteExtension_ClearsActiveState()
    {
        var model = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 42.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var interpreter = new CinematicInterpreter(model);
        interpreter.ActivateContinuationPoint("test_point");

        interpreter.CompleteExtension();

        Assert.False(interpreter.HasActiveExtension);
        Assert.Null(interpreter.ActiveContinuationPoint);
    }

    // =========================================================================
    // RESET TESTS
    // =========================================================================

    [Fact]
    public void Reset_ClearsAllState()
    {
        var model = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 42.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var interpreter = new CinematicInterpreter(model);
        interpreter.SetInput("input0", 100.0);
        interpreter.ActivateContinuationPoint("test_point");

        interpreter.Reset();

        Assert.False(interpreter.HasActiveExtension);
        Assert.Null(interpreter.ActiveContinuationPoint);
        Assert.Equal(0.0, interpreter.InputState[0]); // Reset to default
    }

    // =========================================================================
    // TIMEOUT BEHAVIOR TESTS
    // =========================================================================

    [Fact]
    public void Evaluate_WithTimeoutOverride_UsesOverride()
    {
        var model = CreateModel(
            CreateSimpleBytecode(),
            constants: new[] { 42.0 },
            continuationPoints: new[]
            {
                ContinuationPoint.Create("test_point", 0, TimeSpan.FromSeconds(2), 0, 0)
            },
            strings: new[] { "test_point" });

        var interpreter = new CinematicInterpreter(model);

        // Use a very short timeout override (1ms)
        interpreter.ActivateContinuationPoint("test_point", timeoutMs: 1);

        Assert.True(interpreter.HasActiveExtension);
    }
}
