// =============================================================================
// Behavior Model Interpreter Interface
// DI-friendly interface for behavior model interpreters.
// =============================================================================

namespace BeyondImmersion.Bannou.SDK.Behavior.Runtime;

/// <summary>
/// Interface for behavior model interpreters, enabling DI and testability.
/// </summary>
/// <remarks>
/// <para>
/// The interpreter executes compiled ABML bytecode against a provided input state.
/// Each character should have its own interpreter instance (not thread-safe).
/// </para>
/// <para>
/// This interface enables:
/// - Dependency injection in server-side services
/// - Testability through mocking
/// - Future pooling/caching implementations
/// </para>
/// </remarks>
public interface IBehaviorModelInterpreter
{
    /// <summary>
    /// The model being interpreted.
    /// </summary>
    BehaviorModel Model { get; }

    /// <summary>
    /// Model ID for identification.
    /// </summary>
    Guid ModelId { get; }

    /// <summary>
    /// Input schema for this model.
    /// </summary>
    StateSchema InputSchema { get; }

    /// <summary>
    /// String table for output string lookup.
    /// </summary>
    IReadOnlyList<string> StringTable { get; }

    /// <summary>
    /// Sets the random seed for deterministic execution.
    /// Call before Evaluate() for reproducible results.
    /// </summary>
    /// <param name="seed">Random seed (e.g., frame number for replays).</param>
    void SetRandomSeed(int seed);

    /// <summary>
    /// Evaluates the behavior model with the given input state.
    /// Returns the output state (action intent).
    /// </summary>
    /// <remarks>
    /// This method is allocation-free after initial setup.
    /// </remarks>
    /// <param name="inputState">Current game state values (must match input schema).</param>
    /// <param name="outputState">Pre-allocated output buffer (must match output schema).</param>
    void Evaluate(ReadOnlySpan<double> inputState, Span<double> outputState);

    /// <summary>
    /// Evaluates the behavior model with continuation point pause support.
    /// When a CONTINUATION_POINT opcode is reached, evaluation pauses and
    /// returns a result indicating the pause. Call Resume methods to continue.
    /// </summary>
    /// <param name="inputState">Current game state values (must match input schema).</param>
    /// <param name="outputState">Pre-allocated output buffer (must match output schema).</param>
    /// <returns>Result indicating whether evaluation completed or paused.</returns>
    EvaluationResult EvaluateWithPause(ReadOnlySpan<double> inputState, Span<double> outputState);

    /// <summary>
    /// Gets the current stack depth (for debugging).
    /// </summary>
    int CurrentStackDepth { get; }

    /// <summary>
    /// Gets the current instruction pointer (for debugging).
    /// </summary>
    int CurrentInstructionPointer { get; }

    /// <summary>
    /// Whether evaluation is currently paused at a continuation point.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Index of the continuation point where evaluation is paused (-1 if not paused).
    /// </summary>
    int PausedContinuationPointIndex { get; }

    /// <summary>
    /// Resumes evaluation with the default flow after pausing at a continuation point.
    /// </summary>
    /// <param name="inputState">Current game state values.</param>
    /// <param name="outputState">Pre-allocated output buffer.</param>
    /// <returns>Result of continued evaluation.</returns>
    EvaluationResult ResumeWithDefaultFlow(ReadOnlySpan<double> inputState, Span<double> outputState);

    /// <summary>
    /// Resumes evaluation by executing an extension model instead of the default flow.
    /// </summary>
    /// <param name="extensionInterpreter">The extension interpreter to execute.</param>
    /// <param name="inputState">Current game state values.</param>
    /// <param name="outputState">Pre-allocated output buffer.</param>
    void ResumeWithExtension(
        IBehaviorModelInterpreter extensionInterpreter,
        ReadOnlySpan<double> inputState,
        Span<double> outputState);

    /// <summary>
    /// Clears the pause state without resuming execution.
    /// </summary>
    void ClearPauseState();
}
