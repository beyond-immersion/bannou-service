#nullable enable

using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Dispatches unhandled exceptions to all registered <see cref="IUnhandledExceptionHandler"/> implementations.
/// </summary>
/// <remarks>
/// Inject this interface wherever centralized exception handling is needed — background workers,
/// event handlers, middleware, or any code path where exceptions might otherwise go unobserved.
/// The dispatcher iterates all registered handlers with per-handler fault isolation.
/// </remarks>
public interface IUnhandledExceptionDispatcher
{
    /// <summary>
    /// Dispatches an unhandled exception to all registered handlers.
    /// </summary>
    /// <remarks>
    /// Each handler is invoked independently with its own try/catch. A failing handler
    /// does not prevent subsequent handlers from executing. Handler failures are logged
    /// but never propagated to the caller.
    /// </remarks>
    /// <param name="exception">The unhandled exception to dispatch.</param>
    /// <param name="context">Structured context about where the exception occurred.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when all handlers have finished (or failed).</returns>
    Task DispatchAsync(Exception exception, UnhandledExceptionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IUnhandledExceptionDispatcher"/> that iterates all
/// registered <see cref="IUnhandledExceptionHandler"/> implementations in registration order.
/// </summary>
public sealed class UnhandledExceptionDispatcher : IUnhandledExceptionDispatcher
{
    private readonly IEnumerable<IUnhandledExceptionHandler> _handlers;
    private readonly ILogger<UnhandledExceptionDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="UnhandledExceptionDispatcher"/>.
    /// </summary>
    /// <param name="handlers">All registered exception handlers, in DI registration order.</param>
    /// <param name="logger">Logger for recording handler failures.</param>
    public UnhandledExceptionDispatcher(
        IEnumerable<IUnhandledExceptionHandler> handlers,
        ILogger<UnhandledExceptionDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(
        Exception exception,
        UnhandledExceptionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(exception, context, cancellationToken);
            }
            catch (Exception handlerException)
            {
                // Per-handler fault isolation: log the failure but continue to the next handler
                _logger.LogError(
                    handlerException,
                    "Unhandled exception handler {HandlerType} failed while processing {Operation} in {ServiceName}",
                    handler.GetType().Name,
                    context.Operation,
                    context.ServiceName);
            }
        }
    }
}
