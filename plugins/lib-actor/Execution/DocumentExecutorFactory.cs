// =============================================================================
// Document Executor Factory
// Creates DocumentExecutor instances with cognition handlers registered via DI.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;
using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Actor.Execution;

/// <summary>
/// Factory for creating DocumentExecutor instances with cognition handlers.
/// Handlers are resolved via DI to enable proper dependency injection.
/// </summary>
public sealed class DocumentExecutorFactory : IDocumentExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a new document executor factory.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving handlers.</param>
    public DocumentExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public IDocumentExecutor Create()
    {
        // Create evaluator
        var evaluator = new ExpressionEvaluator();

        // Resolve intent emission registries from DI for domain action integration
        var emitters = _serviceProvider.GetService<IIntentEmitterRegistry>();
        var archetypes = _serviceProvider.GetService<IArchetypeRegistry>();
        var controlGates = _serviceProvider.GetService<IControlGateRegistry>();

        // Create handler registry with built-in handlers and intent emission support
        var handlers = ActionHandlerRegistry.CreateWithBuiltins(emitters, archetypes, controlGates);

        // Register cognition handlers from DI
        // Order: specific handlers before generic, as ActionHandlerRegistry
        // returns the first matching handler (first-registered wins for same action type)
        RegisterCognitionHandlers(handlers);

        return new DocumentExecutor(evaluator, handlers);
    }

    /// <summary>
    /// Registers all cognition handlers from DI into the handler registry.
    /// </summary>
    /// <param name="handlers">The handler registry.</param>
    private void RegisterCognitionHandlers(ActionHandlerRegistry handlers)
    {
        // Stage 1: Attention filtering
        handlers.Register(_serviceProvider.GetRequiredService<FilterAttentionHandler>());

        // Stage 2: Significance assessment
        handlers.Register(_serviceProvider.GetRequiredService<AssessSignificanceHandler>());

        // Stage 3: Memory operations
        handlers.Register(_serviceProvider.GetRequiredService<QueryMemoryHandler>());
        handlers.Register(_serviceProvider.GetRequiredService<StoreMemoryHandler>());

        // Stage 4: Goal impact evaluation
        handlers.Register(_serviceProvider.GetRequiredService<EvaluateGoalImpactHandler>());

        // Stage 5: GOAP replanning
        handlers.Register(_serviceProvider.GetRequiredService<TriggerGoapReplanHandler>());

        // Event Brain action handlers (query_options, emit_perception, etc.)
        // Registered via IActionHandler interface to get all implementations
        var eventBrainHandlers = _serviceProvider.GetServices<IActionHandler>();
        foreach (var handler in eventBrainHandlers)
        {
            handlers.Register(handler);
        }
    }
}
