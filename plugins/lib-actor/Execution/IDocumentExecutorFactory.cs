// =============================================================================
// Document Executor Factory Interface
// Creates DocumentExecutor instances with cognition handlers registered.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Execution;

namespace BeyondImmersion.BannouService.Actor.Execution;

/// <summary>
/// Factory for creating DocumentExecutor instances with cognition handlers registered.
/// </summary>
public interface IDocumentExecutorFactory
{
    /// <summary>
    /// Creates a new DocumentExecutor with all cognition handlers registered.
    /// </summary>
    /// <returns>A configured document executor.</returns>
    IDocumentExecutor Create();
}
