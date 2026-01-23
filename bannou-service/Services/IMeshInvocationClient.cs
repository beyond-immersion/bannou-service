#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Client for invoking methods on other services via the mesh.
/// Provides service-to-service communication through lib-mesh infrastructure.
/// Uses the mesh service for endpoint resolution and YARP for HTTP forwarding.
/// </summary>
public interface IMeshInvocationClient
{
    /// <summary>
    /// Invoke a method on a remote service and deserialize the response.
    /// </summary>
    /// <typeparam name="TRequest">Request body type.</typeparam>
    /// <typeparam name="TResponse">Response body type.</typeparam>
    /// <param name="appId">Target app-id (e.g., "bannou", "auth-service").</param>
    /// <param name="methodName">Method path (e.g., "auth/login", "account/get").</param>
    /// <param name="request">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    /// <exception cref="MeshInvocationException">Thrown when invocation fails.</exception>
    Task<TResponse> InvokeMethodAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;

    /// <summary>
    /// Invoke a method on a remote service without expecting a response body.
    /// </summary>
    /// <typeparam name="TRequest">Request body type.</typeparam>
    /// <param name="appId">Target app-id.</param>
    /// <param name="methodName">Method path.</param>
    /// <param name="request">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="MeshInvocationException">Thrown when invocation fails.</exception>
    Task InvokeMethodAsync<TRequest>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class;

    /// <summary>
    /// Invoke a method on a remote service with full response details.
    /// </summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    /// <exception cref="MeshInvocationException">Thrown when invocation fails.</exception>
    Task<HttpResponseMessage> InvokeMethodWithResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create an HTTP request message for invoking a method.
    /// The request can be customized before sending via InvokeMethodWithResponseAsync.
    /// </summary>
    /// <param name="httpMethod">HTTP method to use.</param>
    /// <param name="appId">Target app-id.</param>
    /// <param name="methodName">Method path.</param>
    /// <returns>Configured HTTP request message.</returns>
    HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName);

    /// <summary>
    /// Create an HTTP request message with a typed body.
    /// </summary>
    /// <typeparam name="TRequest">Request body type.</typeparam>
    /// <param name="httpMethod">HTTP method to use.</param>
    /// <param name="appId">Target app-id.</param>
    /// <param name="methodName">Method path.</param>
    /// <param name="request">Request body to serialize.</param>
    /// <returns>Configured HTTP request message with body.</returns>
    HttpRequestMessage CreateInvokeMethodRequest<TRequest>(
        HttpMethod httpMethod,
        string appId,
        string methodName,
        TRequest request)
        where TRequest : class;

    /// <summary>
    /// Invoke a method on a remote service using GET (no request body).
    /// </summary>
    /// <typeparam name="TResponse">Response body type.</typeparam>
    /// <param name="appId">Target app-id.</param>
    /// <param name="methodName">Method path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    /// <exception cref="MeshInvocationException">Thrown when invocation fails.</exception>
    Task<TResponse> InvokeMethodAsync<TResponse>(
        string appId,
        string methodName,
        CancellationToken cancellationToken = default)
        where TResponse : class;

    /// <summary>
    /// Check if a target app-id has any healthy endpoints available.
    /// </summary>
    /// <param name="appId">Target app-id to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if at least one healthy endpoint exists.</returns>
    Task<bool> IsServiceAvailableAsync(
        string appId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when mesh invocation fails.
/// </summary>
public class MeshInvocationException : Exception
{
    /// <summary>
    /// The target app-id of the failed invocation.
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// The method name that was being invoked.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// HTTP status code if available.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Creates a new MeshInvocationException.
    /// </summary>
    public MeshInvocationException(
        string appId,
        string methodName,
        string message,
        Exception? innerException = null,
        int? statusCode = null)
        : base($"Failed to invoke {methodName} on {appId}: {message}", innerException)
    {
        AppId = appId;
        MethodName = methodName;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Creates a MeshInvocationException for when no endpoints are available.
    /// </summary>
    public static MeshInvocationException NoEndpointsAvailable(string appId, string methodName)
        => new(appId, methodName, "No healthy endpoints available", statusCode: 503);

    /// <summary>
    /// Creates a MeshInvocationException for when the circuit breaker is open.
    /// </summary>
    public static MeshInvocationException CircuitBreakerOpen(string appId, string methodName)
        => new(appId, methodName, "Circuit breaker is open - service appears unhealthy", statusCode: 503);

    /// <summary>
    /// Creates a MeshInvocationException for HTTP errors.
    /// </summary>
    public static MeshInvocationException HttpError(
        string appId,
        string methodName,
        int statusCode,
        string? responseBody = null)
        => new(appId, methodName, $"HTTP {statusCode}: {responseBody ?? "No response body"}", statusCode: statusCode);
}
