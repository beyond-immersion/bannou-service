using Microsoft.AspNetCore.Http;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Middleware that extracts service context headers from incoming requests
/// and stores them in ServiceRequestContext for use by downstream services.
/// </summary>
/// <remarks>
/// This middleware captures:
/// - X-Bannou-Session-Id: WebSocket session ID (set by Connect when proxying)
/// - X-Correlation-Id: Optional distributed tracing ID
///
/// The context is automatically cleared after the request completes.
/// </remarks>
public class ServiceRequestContextMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Header name for session ID propagation.
    /// </summary>
    public const string SessionIdHeader = "X-Bannou-Session-Id";

    /// <summary>
    /// Header name for correlation ID propagation.
    /// </summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>
    /// Creates a new instance of the middleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public ServiceRequestContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Extract session ID from incoming request (set by Connect)
            if (context.Request.Headers.TryGetValue(SessionIdHeader, out var sessionId))
            {
                ServiceRequestContext.SessionId = sessionId.FirstOrDefault();
            }

            // Extract correlation ID if present
            if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId))
            {
                ServiceRequestContext.CorrelationId = correlationId.FirstOrDefault();
            }

            await _next(context);
        }
        finally
        {
            // Always clear context after request completes
            ServiceRequestContext.Clear();
        }
    }
}

/// <summary>
/// Extension methods for registering ServiceRequestContextMiddleware.
/// </summary>
public static class ServiceRequestContextMiddlewareExtensions
{
    /// <summary>
    /// Adds the ServiceRequestContextMiddleware to the application pipeline.
    /// Should be added early in the pipeline to capture context for all requests.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseServiceRequestContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ServiceRequestContextMiddleware>();
    }
}
