#nullable enable

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Context for an unhandled exception, providing structured information about
/// where the exception occurred for handlers to use in logging, telemetry, or event publishing.
/// </summary>
/// <param name="ServiceName">Logical service name where the exception occurred (e.g., "account", "auth").</param>
/// <param name="Operation">Operation or method name that failed (e.g., "GetAccount", "HealthCheck").</param>
/// <param name="Endpoint">Optional endpoint being called (e.g., "post:account/get").</param>
/// <param name="CorrelationId">Optional correlation ID for distributed tracing.</param>
public record UnhandledExceptionContext(
    string ServiceName,
    string Operation,
    string? Endpoint = null,
    Guid? CorrelationId = null);

/// <summary>
/// Handler for unhandled exceptions that escape normal try/catch blocks.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables a composite exception handling pattern where multiple
/// implementations all fire for each unhandled exception. Handlers are discovered
/// via <c>IEnumerable&lt;IUnhandledExceptionHandler&gt;</c> DI injection and invoked
/// by <see cref="BeyondImmersion.BannouService.Services.IUnhandledExceptionDispatcher"/>.
/// </para>
/// <para>
/// <b>Registration Order</b>: Handlers fire in DI registration order, which follows
/// plugin loading order (L0 infrastructure first, then L1, L2, etc.). The default
/// <c>LoggingUnhandledExceptionHandler</c> is registered before any plugins load.
/// </para>
/// <para>
/// <b>Fault Isolation</b>: The dispatcher catches exceptions thrown by individual handlers,
/// so one failing handler does not prevent others from executing.
/// </para>
/// <para>
/// <b>Implementation Guidelines</b>:
/// </para>
/// <list type="bullet">
///   <item>Handlers should not throw exceptions; if they do, the dispatcher catches and logs them</item>
///   <item>Handlers that publish events must guard against infinite recursion (e.g., if publishing itself throws)</item>
///   <item>Handlers should be fast — avoid blocking I/O where possible</item>
/// </list>
/// <para>
/// <b>Built-in Handlers</b>:
/// </para>
/// <list type="bullet">
///   <item><c>LoggingUnhandledExceptionHandler</c> — logs at Error level (always registered)</item>
///   <item><c>MessagingUnhandledExceptionHandler</c> — publishes error events via IMessageBus (registered by lib-messaging)</item>
/// </list>
/// </remarks>
public interface IUnhandledExceptionHandler
{
    /// <summary>
    /// Handles an unhandled exception with the given context.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="context">Structured context about where the exception occurred.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when handling is finished.</returns>
    Task HandleAsync(Exception exception, UnhandledExceptionContext context, CancellationToken cancellationToken);
}
