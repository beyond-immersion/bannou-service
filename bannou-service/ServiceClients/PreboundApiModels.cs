#nullable enable

using System.Text.Json;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Definition of a prebound API call.
/// Decoupled from generated contract models for use across services.
/// </summary>
/// <remarks>
/// This model is used by ServiceNavigator.ExecutePreboundApiAsync to define
/// what service/endpoint to call and what payload template to use.
/// </remarks>
public class PreboundApiDefinition
{
    /// <summary>
    /// Target service name (e.g., "currency", "inventory", "character").
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Target endpoint path (e.g., "/currency/transfer", "/inventory/add").
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// JSON payload template with {{variable}} placeholders.
    /// Variables are substituted from the context dictionary at execution time.
    /// </summary>
    public string PayloadTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of what this API call does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// How to execute this API call.
    /// </summary>
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.Sync;
}

/// <summary>
/// How a prebound API should be executed.
/// </summary>
public enum ExecutionMode
{
    /// <summary>Execute synchronously and wait for response.</summary>
    Sync,

    /// <summary>Execute asynchronously but still track completion.</summary>
    Async,

    /// <summary>Execute and don't wait or track - fire and forget.</summary>
    FireAndForget
}

/// <summary>
/// How to execute a batch of prebound APIs.
/// </summary>
public enum BatchExecutionMode
{
    /// <summary>Execute all APIs in parallel.</summary>
    Parallel,

    /// <summary>Execute APIs sequentially, continuing even if some fail.</summary>
    Sequential,

    /// <summary>Execute APIs sequentially, stopping on first failure.</summary>
    SequentialStopOnFailure
}

/// <summary>
/// Result of executing a raw API call.
/// </summary>
/// <remarks>
/// This is the low-level result returned by ExecuteRawApiAsync.
/// Contains the HTTP response details without any template substitution info.
/// </remarks>
public class RawApiResult
{
    /// <summary>
    /// HTTP status code returned by the service.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Whether the request was successful (2xx status code).
    /// </summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

    /// <summary>
    /// Raw response body as string.
    /// May be null if the response had no body or couldn't be read.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// Parsed response as JsonDocument.
    /// Lazy-loaded from ResponseBody. May be null if response isn't valid JSON.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for disposing this JsonDocument if not null.
    /// </remarks>
    public JsonDocument? ResponseDocument { get; init; }

    /// <summary>
    /// Response headers from the service.
    /// </summary>
    public IReadOnlyDictionary<string, IEnumerable<string>>? Headers { get; init; }

    /// <summary>
    /// How long the request took to complete.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Error message if the request failed.
    /// This is set for network errors, timeouts, etc. - not HTTP error statuses.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Exception that was thrown, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// The service name that was called.
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// The endpoint path that was called.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static RawApiResult Success(
        int statusCode,
        string? responseBody,
        TimeSpan duration,
        string? serviceName = null,
        string? endpoint = null,
        IReadOnlyDictionary<string, IEnumerable<string>>? headers = null)
    {
        JsonDocument? doc = null;
        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                doc = JsonDocument.Parse(responseBody);
            }
            catch
            {
                // Response isn't valid JSON - that's OK, leave doc null
            }
        }

        return new RawApiResult
        {
            StatusCode = statusCode,
            ResponseBody = responseBody,
            ResponseDocument = doc,
            Duration = duration,
            ServiceName = serviceName,
            Endpoint = endpoint,
            Headers = headers
        };
    }

    /// <summary>
    /// Creates a failure result for network/transport errors.
    /// </summary>
    public static RawApiResult Failure(
        string errorMessage,
        Exception? exception = null,
        TimeSpan duration = default,
        string? serviceName = null,
        string? endpoint = null,
        int statusCode = 0)
    {
        return new RawApiResult
        {
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            Exception = exception,
            Duration = duration,
            ServiceName = serviceName,
            Endpoint = endpoint
        };
    }
}

/// <summary>
/// Result of executing a prebound API with variable substitution.
/// </summary>
/// <remarks>
/// Wraps RawApiResult with additional information about the template substitution.
/// </remarks>
public class PreboundApiResult
{
    /// <summary>
    /// The API definition that was executed.
    /// </summary>
    public PreboundApiDefinition Api { get; init; } = default!;

    /// <summary>
    /// The JSON payload after variable substitution.
    /// This is what was actually sent to the service.
    /// </summary>
    public string SubstitutedPayload { get; init; } = string.Empty;

    /// <summary>
    /// The raw API result from executing the request.
    /// </summary>
    public RawApiResult Result { get; init; } = default!;

    /// <summary>
    /// Whether template variable substitution succeeded.
    /// If false, the API was not called.
    /// </summary>
    public bool SubstitutionSucceeded { get; init; }

    /// <summary>
    /// Error message if substitution failed.
    /// </summary>
    public string? SubstitutionError { get; init; }

    /// <summary>
    /// Whether the overall operation succeeded (substitution + API call).
    /// </summary>
    public bool IsSuccess => SubstitutionSucceeded && Result.IsSuccess;

    /// <summary>
    /// Creates a result for successful substitution and execution.
    /// </summary>
    public static PreboundApiResult Success(
        PreboundApiDefinition api,
        string substitutedPayload,
        RawApiResult result)
    {
        return new PreboundApiResult
        {
            Api = api,
            SubstitutedPayload = substitutedPayload,
            Result = result,
            SubstitutionSucceeded = true
        };
    }

    /// <summary>
    /// Creates a result for substitution failure (API was not called).
    /// </summary>
    public static PreboundApiResult SubstitutionFailed(
        PreboundApiDefinition api,
        string error)
    {
        return new PreboundApiResult
        {
            Api = api,
            SubstitutedPayload = string.Empty,
            Result = RawApiResult.Failure($"Template substitution failed: {error}"),
            SubstitutionSucceeded = false,
            SubstitutionError = error
        };
    }
}
