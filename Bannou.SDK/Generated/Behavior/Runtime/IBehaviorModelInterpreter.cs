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
    /// Gets the current stack depth (for debugging).
    /// </summary>
    int CurrentStackDepth { get; }

    /// <summary>
    /// Gets the current instruction pointer (for debugging).
    /// </summary>
    int CurrentInstructionPointer { get; }
}
