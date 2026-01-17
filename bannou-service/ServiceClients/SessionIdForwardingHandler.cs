using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// HTTP message handler that automatically forwards the session ID header
/// to downstream service calls when making mesh requests.
/// </summary>
/// <remarks>
/// This handler reads from ServiceRequestContext.SessionId and attaches
/// the X-Bannou-Session-Id header to outgoing requests, enabling full
/// request chain tracing through distributed service calls.
///
/// Add this handler to HttpClient via .AddHttpMessageHandler() in DI registration.
/// </remarks>
public class SessionIdForwardingHandler : DelegatingHandler
{
    private readonly ILogger<SessionIdForwardingHandler> _logger;

    /// <summary>
    /// Creates a new instance of the handler.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SessionIdForwardingHandler(ILogger<SessionIdForwardingHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends an HTTP request, adding the session ID header if available in context.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Forward session ID if present in context and not already set on request
        var sessionId = ServiceRequestContext.SessionId;
        if (!string.IsNullOrEmpty(sessionId) &&
            !request.Headers.Contains(ServiceRequestContextMiddleware.SessionIdHeader))
        {
            request.Headers.Add(ServiceRequestContextMiddleware.SessionIdHeader, sessionId);
            _logger.LogTrace(
                "Forwarding session ID {SessionId} to {Method} {Uri}",
                sessionId, request.Method, request.RequestUri);
        }

        // Forward correlation ID if present
        var correlationId = ServiceRequestContext.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId) &&
            !request.Headers.Contains(ServiceRequestContextMiddleware.CorrelationIdHeader))
        {
            request.Headers.Add(ServiceRequestContextMiddleware.CorrelationIdHeader, correlationId);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
