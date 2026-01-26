// =============================================================================
// Behavior Model Interpreter Factory Interface
// Factory for creating behavior model interpreters.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.Behavior.Runtime;

/// <summary>
/// Factory for creating behavior model interpreters.
/// Enables DI injection and potentially pooling in the future.
/// </summary>
/// <remarks>
/// <para>
/// Use this factory to create interpreters rather than instantiating
/// BehaviorModelInterpreter directly. This enables:
/// - Dependency injection in services
/// - Future interpreter pooling
/// - Testing with mock interpreters
/// </para>
/// </remarks>
public interface IBehaviorModelInterpreterFactory
{
    /// <summary>
    /// Creates a new interpreter for the given behavior model.
    /// </summary>
    /// <param name="model">The compiled behavior model to interpret.</param>
    /// <returns>A new interpreter instance.</returns>
    IBehaviorModelInterpreter Create(BehaviorModel model);
}
