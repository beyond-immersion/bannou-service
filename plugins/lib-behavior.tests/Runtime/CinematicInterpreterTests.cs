// =============================================================================
// Cinematic Interpreter Tests
// Tests for pause/resume functionality and extension injection.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.BannouService.Behavior.Runtime;
using BeyondImmersion.BannouService.Events;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Runtime;

/// <summary>
/// Tests for the CinematicInterpreter pause/resume and extension injection functionality.
/// </summary>
public class CinematicInterpreterTests
{
    private readonly BehaviorCompiler _compiler = new();

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Compiles a YAML behavior to a BehaviorModel.
    /// </summary>
    private BehaviorModel CompileYaml(string yaml)
    {
        var result = _compiler.CompileYaml(yaml);
        if (!result.Success || result.Bytecode is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Compilation failed: {errors}");
        }
        return BehaviorModel.Deserialize(result.Bytecode);
    }

    /// <summary>
    /// Simulates receiving a CinematicExtensionAvailableEvent and injecting it.
    /// This is a test helper - may move to SDK later per plan.
    /// </summary>
    private static bool SimulateExtensionDelivery(
        CinematicInterpreter interpreter,
        CinematicExtensionAvailableEvent evt,
        BehaviorModel extensionModel)
    {
        // Validate event matches current waiting state
        if (!interpreter.IsWaitingForExtension ||
            interpreter.WaitingContinuationPoint != evt.ContinuationPointName)
        {
            return false;
        }

        return interpreter.InjectExtension(evt.ContinuationPointName, extensionModel);
    }

    // =========================================================================
    // INTERPRETER PAUSE TESTS
    // =========================================================================

    /// <summary>
    /// Verifies that EvaluateWithPause returns paused status at continuation point.
    /// </summary>
    [Fact]
    public void EvaluateWithPause_AtContinuationPoint_ReturnsPaused()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var interpreter = new BehaviorModelInterpreter(model);
        var inputState = new double[model.Schema.InputCount];
        var outputState = new double[model.Schema.OutputCount];

        // Initialize with defaults
        for (var i = 0; i < model.Schema.Inputs.Count; i++)
        {
            inputState[i] = model.Schema.Inputs[i].DefaultValue;
        }

        // Act
        var result = interpreter.EvaluateWithPause(inputState, outputState);

        // Assert
        Assert.True(result.IsPaused, "Should be paused at continuation point");
        Assert.True(interpreter.IsPaused, "Interpreter should be in paused state");
        Assert.Equal(EvaluationStatus.PausedAtContinuationPoint, result.Status);
        Assert.True(result.ContinuationPointIndex >= 0, "Should have valid continuation point index");
        Assert.True(result.TimeoutMs > 0, "Should have timeout value");
    }

    /// <summary>
    /// Verifies that output is set before reaching the continuation point.
    /// </summary>
    [Fact]
    public void EvaluateWithPause_SetsOutputBeforePause()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var interpreter = new BehaviorModelInterpreter(model);
        var inputState = new double[model.Schema.InputCount];
        var outputState = new double[model.Schema.OutputCount];

        for (var i = 0; i < model.Schema.Inputs.Count; i++)
        {
            inputState[i] = model.Schema.Inputs[i].DefaultValue;
        }

        // Act
        interpreter.EvaluateWithPause(inputState, outputState);

        // Assert - before_cp should be set to 1.0 before the continuation point
        var beforeCpIndex = GetOutputIndex(model, "before_cp");
        Assert.Equal(1.0, outputState[beforeCpIndex]);
    }

    /// <summary>
    /// Verifies ResumeWithDefaultFlow continues from the correct offset.
    /// </summary>
    [Fact]
    public void ResumeWithDefaultFlow_ContinuesFromCorrectOffset()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var interpreter = new BehaviorModelInterpreter(model);
        var inputState = new double[model.Schema.InputCount];
        var outputState = new double[model.Schema.OutputCount];

        for (var i = 0; i < model.Schema.Inputs.Count; i++)
        {
            inputState[i] = model.Schema.Inputs[i].DefaultValue;
        }

        // First, evaluate to pause at continuation point
        var pauseResult = interpreter.EvaluateWithPause(inputState, outputState);
        Assert.True(pauseResult.IsPaused, "Should pause at continuation point");

        // Act - resume with default flow
        var resumeResult = interpreter.ResumeWithDefaultFlow(inputState, outputState);

        // Assert
        Assert.False(resumeResult.IsPaused, "Should not be paused after resume");
        Assert.False(interpreter.IsPaused, "Interpreter should not be paused");

        // Default flow sets after_cp to 2.0
        var afterCpIndex = GetOutputIndex(model, "after_cp");
        Assert.Equal(2.0, outputState[afterCpIndex]);
    }

    /// <summary>
    /// Verifies ResumeWithExtension executes the extension bytecode.
    /// </summary>
    [Fact]
    public void ResumeWithExtension_ExecutesExtensionBytecode()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var baseInterpreter = new BehaviorModelInterpreter(baseModel);
        var extInterpreter = new BehaviorModelInterpreter(extModel);

        var inputState = new double[baseModel.Schema.InputCount];
        var outputState = new double[baseModel.Schema.OutputCount];

        for (var i = 0; i < baseModel.Schema.Inputs.Count; i++)
        {
            inputState[i] = baseModel.Schema.Inputs[i].DefaultValue;
        }

        // First, evaluate to pause at continuation point
        var pauseResult = baseInterpreter.EvaluateWithPause(inputState, outputState);
        Assert.True(pauseResult.IsPaused, "Should pause at continuation point");

        // Act - resume with extension
        baseInterpreter.ResumeWithExtension(extInterpreter, inputState, outputState);

        // Assert - extension sets after_cp to 99.0
        var afterCpIndex = GetOutputIndex(baseModel, "after_cp");
        Assert.Equal(99.0, outputState[afterCpIndex]);
    }

    // =========================================================================
    // CINEMATIC INTERPRETER TESTS
    // =========================================================================

    /// <summary>
    /// Verifies that CinematicInterpreter correctly pauses at continuation point.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_Evaluate_PausesAtContinuationPoint()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var cinematic = new CinematicInterpreter(model);

        // Act
        var result = cinematic.Evaluate();

        // Assert
        Assert.True(result.IsWaiting, "Should be waiting for extension");
        Assert.True(cinematic.IsWaitingForExtension, "Should be in waiting state");
        Assert.Equal("dramatic_pause", cinematic.WaitingContinuationPoint);
        Assert.Equal(CinematicStatus.WaitingForExtension, result.Status);
    }

    /// <summary>
    /// Verifies that pre-registered extension executes immediately.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_PreRegisteredExtension_ExecutesImmediately()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var cinematic = new CinematicInterpreter(baseModel);
        cinematic.RegisterExtension("dramatic_pause", extModel);

        // Act
        var result = cinematic.Evaluate();

        // Assert
        Assert.True(result.ExtensionWasExecuted, "Extension should have been executed");
        Assert.Equal(CinematicStatus.ExtensionExecuted, result.Status);
        Assert.Equal("dramatic_pause", result.ContinuationPointName);

        // Extension sets after_cp to 99.0
        Assert.Equal(99.0, cinematic.GetOutput("after_cp"));
    }

    /// <summary>
    /// Verifies that timeout triggers default flow.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_Timeout_TriggersDefaultFlow()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var cinematic = new CinematicInterpreter(model);

        // First evaluate to start waiting
        var waitResult = cinematic.Evaluate();
        Assert.True(waitResult.IsWaiting, "Should be waiting");

        // Wait for timeout (500ms in fixture)
        Thread.Sleep(600);

        // Act - evaluate again after timeout
        var result = cinematic.Evaluate();

        // Assert - should have completed with default flow
        Assert.True(result.IsCompleted, "Should be completed after timeout");
        Assert.Contains("Timeout", result.Message ?? "");

        // Default flow sets after_cp to 2.0
        Assert.Equal(2.0, cinematic.GetOutput("after_cp"));
    }

    /// <summary>
    /// Verifies that extension injection works mid-cinematic.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_ExtensionInjection_WorksMidCinematic()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var cinematic = new CinematicInterpreter(baseModel);

        // First evaluate to start waiting
        var waitResult = cinematic.Evaluate();
        Assert.True(waitResult.IsWaiting, "Should be waiting");

        // Act - inject extension while waiting
        var injected = cinematic.InjectExtension("dramatic_pause", extModel);
        Assert.True(injected, "Extension should be accepted");

        // Evaluate again to process the injected extension
        var result = cinematic.Evaluate();

        // Assert
        Assert.True(result.ExtensionWasExecuted, "Extension should be executed");

        // Extension sets after_cp to 99.0
        Assert.Equal(99.0, cinematic.GetOutput("after_cp"));
    }

    /// <summary>
    /// Verifies that InjectExtension returns false when not waiting at matching point.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_InjectExtension_ReturnsFalseWhenNotWaiting()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var cinematic = new CinematicInterpreter(baseModel);

        // Act - try to inject before evaluation (not waiting yet)
        var injected = cinematic.InjectExtension("dramatic_pause", extModel);

        // Assert - should register for future use, but return false since not currently waiting
        Assert.False(injected);
        Assert.False(cinematic.IsWaitingForExtension);
    }

    /// <summary>
    /// Verifies ForceResumeWithDefaultFlow works.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_ForceResume_BypassesTimeout()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var cinematic = new CinematicInterpreter(model);

        // Start waiting
        var waitResult = cinematic.Evaluate();
        Assert.True(waitResult.IsWaiting, "Should be waiting");

        // Act - force resume (don't wait for timeout)
        var resumed = cinematic.ForceResumeWithDefaultFlow();

        // Assert
        Assert.True(resumed, "Should have resumed");
        Assert.False(cinematic.IsWaitingForExtension, "Should not be waiting anymore");

        // Default flow sets after_cp to 2.0
        Assert.Equal(2.0, cinematic.GetOutput("after_cp"));
    }

    /// <summary>
    /// Verifies Reset clears pause state.
    /// </summary>
    [Fact]
    public void CinematicInterpreter_Reset_ClearsPauseState()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var cinematic = new CinematicInterpreter(model);

        // Start waiting
        cinematic.Evaluate();
        Assert.True(cinematic.IsWaitingForExtension, "Should be waiting");

        // Act
        cinematic.Reset();

        // Assert
        Assert.False(cinematic.IsWaitingForExtension, "Should not be waiting after reset");
        Assert.Null(cinematic.WaitingContinuationPoint);
    }

    // =========================================================================
    // EVENT SIMULATION TESTS
    // =========================================================================

    /// <summary>
    /// Verifies that simulated event delivery works correctly.
    /// </summary>
    [Fact]
    public void SimulateExtensionDelivery_AcceptsMatchingEvent()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var cinematic = new CinematicInterpreter(baseModel);

        // Start waiting
        var waitResult = cinematic.Evaluate();
        Assert.True(waitResult.IsWaiting, "Should be waiting");

        // Create event
        var evt = new CinematicExtensionAvailableEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = Guid.NewGuid(),
            CinematicId = baseModel.Id,
            ContinuationPointName = "dramatic_pause",
            ExtensionBytecode = Array.Empty<byte>() // Not used in simulation
        };

        // Act
        var accepted = SimulateExtensionDelivery(cinematic, evt, extModel);

        // Assert
        Assert.True(accepted, "Event should be accepted");

        // Evaluate to execute the injected extension
        var result = cinematic.Evaluate();
        Assert.True(result.ExtensionWasExecuted, "Extension should be executed");
        Assert.Equal(99.0, cinematic.GetOutput("after_cp"));
    }

    /// <summary>
    /// Verifies that mismatched continuation point name rejects event.
    /// </summary>
    [Fact]
    public void SimulateExtensionDelivery_RejectsMismatchedContinuationPoint()
    {
        // Arrange
        var baseYaml = RuntimeTestFixtures.Load("cinematic_base");
        var extYaml = RuntimeTestFixtures.Load("cinematic_extension");
        var baseModel = CompileYaml(baseYaml);
        var extModel = CompileYaml(extYaml);

        var cinematic = new CinematicInterpreter(baseModel);

        // Start waiting
        cinematic.Evaluate();

        // Create event with wrong continuation point name
        var evt = new CinematicExtensionAvailableEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = Guid.NewGuid(),
            CinematicId = baseModel.Id,
            ContinuationPointName = "wrong_name",
            ExtensionBytecode = Array.Empty<byte>()
        };

        // Act
        var accepted = SimulateExtensionDelivery(cinematic, evt, extModel);

        // Assert
        Assert.False(accepted, "Event should be rejected due to wrong continuation point");
    }

    // =========================================================================
    // HELPER
    // =========================================================================

    private static int GetOutputIndex(BehaviorModel model, string name)
    {
        var hash = ComputeNameHash(name);
        if (model.Schema.TryGetOutputIndex(hash, out var index))
        {
            return index;
        }
        throw new ArgumentException($"Output '{name}' not found");
    }

    /// <summary>
    /// FNV-1a hash for variable name lookup.
    /// </summary>
    private static uint ComputeNameHash(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }
}
