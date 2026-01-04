// =============================================================================
// Behavior Model Interpreter Factory
// Default implementation of the interpreter factory.
// =============================================================================

namespace BeyondImmersion.Bannou.SDK.Behavior.Runtime;

/// <summary>
/// Default factory for creating behavior model interpreters.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates new BehaviorModelInterpreter instances on demand.
/// Each call creates a fresh interpreter with pre-allocated execution state.
/// </para>
/// <para>
/// Future enhancements could include:
/// - Interpreter pooling for reduced allocations
/// - Instrumented interpreters for profiling
/// - Debug interpreters with breakpoint support
/// </para>
/// </remarks>
public sealed class BehaviorModelInterpreterFactory : IBehaviorModelInterpreterFactory
{
    /// <inheritdoc/>
    public IBehaviorModelInterpreter Create(BehaviorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new BehaviorModelInterpreter(model);
    }
}
