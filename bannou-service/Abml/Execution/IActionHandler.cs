// ═══════════════════════════════════════════════════════════════════════════
// ABML Action Handler Interface
// Interface for action execution handlers.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Interface for action handlers.
/// </summary>
public interface IActionHandler
{
    /// <summary>
    /// Determines if this handler can execute the given action.
    /// </summary>
    /// <param name="action">The action to check.</param>
    /// <returns>True if this handler can execute the action.</returns>
    bool CanHandle(ActionNode action);

    /// <summary>
    /// Executes the action.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="context">Execution context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of execution.</returns>
    ValueTask<ActionResult> ExecuteAsync(ActionNode action, ExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Registry for action handlers.
/// </summary>
public interface IActionHandlerRegistry
{
    /// <summary>
    /// Registers an action handler.
    /// </summary>
    /// <param name="handler">The handler to register.</param>
    void Register(IActionHandler handler);

    /// <summary>
    /// Gets the handler for an action.
    /// </summary>
    /// <param name="action">The action to find a handler for.</param>
    /// <returns>The handler, or null if no handler found.</returns>
    IActionHandler? GetHandler(ActionNode action);
}

/// <summary>
/// Default implementation of action handler registry.
/// </summary>
public sealed class ActionHandlerRegistry : IActionHandlerRegistry
{
    private readonly List<IActionHandler> _handlers = [];

    /// <summary>
    /// Registers an action handler.
    /// </summary>
    public void Register(IActionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
    }

    /// <summary>
    /// Gets the handler for an action.
    /// </summary>
    public IActionHandler? GetHandler(ActionNode action)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(action))
            {
                return handler;
            }
        }
        return null;
    }

    /// <summary>
    /// Creates a registry with all built-in handlers registered.
    /// </summary>
    /// <param name="emitters">Optional intent emitter registry for domain action integration.</param>
    /// <param name="archetypes">Optional archetype registry for entity type resolution.</param>
    /// <param name="controlGates">Optional control gate registry for emission filtering.</param>
    /// <returns>A registry with all built-in handlers.</returns>
    public static ActionHandlerRegistry CreateWithBuiltins(
        IIntentEmitterRegistry? emitters = null,
        IArchetypeRegistry? archetypes = null,
        IControlGateRegistry? controlGates = null)
    {
        var registry = new ActionHandlerRegistry();
        registry.RegisterBuiltinHandlers(emitters, archetypes, controlGates);
        return registry;
    }

    /// <summary>
    /// Registers all built-in control flow and variable handlers.
    /// </summary>
    /// <param name="emitters">Optional intent emitter registry for domain action integration.</param>
    /// <param name="archetypes">Optional archetype registry for entity type resolution.</param>
    /// <param name="controlGates">Optional control gate registry for emission filtering.</param>
    public void RegisterBuiltinHandlers(
        IIntentEmitterRegistry? emitters = null,
        IArchetypeRegistry? archetypes = null,
        IControlGateRegistry? controlGates = null)
    {
        Register(new Handlers.CondHandler());
        Register(new Handlers.ForEachHandler());
        Register(new Handlers.RepeatHandler());
        Register(new Handlers.GotoHandler());
        Register(new Handlers.CallHandler());
        Register(new Handlers.ReturnHandler());
        Register(new Handlers.SetHandler());
        Register(new Handlers.LocalHandler());
        Register(new Handlers.GlobalHandler());
        Register(new Handlers.NumericOperationHandler());
        Register(new Handlers.ClearHandler());
        Register(new Handlers.LogHandler());

        // Register domain action handler with or without intent emission support
        if (emitters != null && archetypes != null && controlGates != null)
        {
            Register(new Handlers.DomainActionHandler((IIntentEmitterRegistry)emitters, (IArchetypeRegistry)archetypes, (IControlGateRegistry)controlGates));
        }
        else
        {
            Register(new Handlers.DomainActionHandler());
        }
    }
}
