#nullable enable

using BeyondImmersion.BannouService.Utilities;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Partial class implementing raw API execution methods for ServiceNavigator.
/// </summary>
/// <remarks>
/// These methods enable direct service invocation with JSON or byte payloads,
/// supporting both typed prebound API execution with template substitution
/// and zero-copy byte forwarding for Connect service routing.
/// </remarks>
public partial class ServiceNavigator
{
    /// <summary>
    /// HTTP client name for raw API execution.
    /// </summary>
    private const string RawApiHttpClientName = "ServiceNavigator.RawApi";

    /// <summary>
    /// Cached JSON content type header for reuse.
    /// </summary>
    private static readonly MediaTypeHeaderValue s_jsonContentType = new("application/json") { CharSet = "utf-8" };

    /// <summary>
    /// Cached JSON accept header for reuse.
    /// </summary>
    private static readonly MediaTypeWithQualityHeaderValue s_jsonAcceptHeader = new("application/json");

    // ═══════════════════════════════════════════════════════════════════════════
    // Raw API Execution Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        string jsonPayload,
        HttpMethod? method = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(endpoint);

        var payloadBytes = string.IsNullOrEmpty(jsonPayload)
            ? ReadOnlyMemory<byte>.Empty
            : Encoding.UTF8.GetBytes(jsonPayload);

        return await ExecuteRawApiAsync(serviceName, endpoint, payloadBytes, method, ct);
    }

    /// <inheritdoc />
    public async Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        ReadOnlyMemory<byte> payload,
        HttpMethod? method = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(endpoint);

        var httpMethod = method ?? HttpMethod.Post;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve app-id for routing
            var appId = _appMappingResolver.GetAppIdForService(serviceName);

            // Build mesh URL using direct path
            var bannouHttpEndpoint = _configuration.EffectiveHttpEndpoint;
            var meshUrl = $"{bannouHttpEndpoint}/{endpoint.TrimStart('/')}";

            // Create HTTP request with proper headers
            using var request = new HttpRequestMessage(httpMethod, meshUrl);

            // Add bannou-app-id header for routing (like ServiceClientBase.PrepareRequest)
            request.Headers.Add("bannou-app-id", appId);
            request.Headers.Accept.Add(s_jsonAcceptHeader);

            // Pass session context for tracing (if available)
            var sessionId = ServiceRequestContext.SessionId;
            if (!string.IsNullOrEmpty(sessionId))
            {
                request.Headers.Add("X-Bannou-Session-Id", sessionId);
            }

            // Add correlation ID if available
            var correlationId = ServiceRequestContext.CorrelationId;
            if (!string.IsNullOrEmpty(correlationId))
            {
                request.Headers.Add("X-Correlation-Id", correlationId);
            }

            // Set payload content if provided and method supports body
            if (payload.Length > 0 && (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Patch))
            {
                var content = new ByteArrayContent(payload.ToArray());
                content.Headers.ContentType = s_jsonContentType;
                request.Content = content;
            }

            // Create HttpClient from factory (properly pooled)
            using var httpClient = _httpClientFactory.CreateClient(RawApiHttpClientName);

            // Execute the request
            using var response = await httpClient.SendAsync(request, ct);

            stopwatch.Stop();

            // Read response content
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            // Extract response headers
            var headers = new Dictionary<string, IEnumerable<string>>();
            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value;
            }
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = header.Value;
            }

            return RawApiResult.Success(
                statusCode: (int)response.StatusCode,
                responseBody: responseBody,
                duration: stopwatch.Elapsed,
                serviceName: serviceName,
                endpoint: endpoint,
                headers: headers);
        }
        catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException || !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return RawApiResult.Failure(
                errorMessage: $"Request timed out for {serviceName} {endpoint}",
                exception: tcEx,
                duration: stopwatch.Elapsed,
                serviceName: serviceName,
                endpoint: endpoint,
                statusCode: 504); // Gateway Timeout
        }
        catch (TaskCanceledException tcEx)
        {
            stopwatch.Stop();
            return RawApiResult.Failure(
                errorMessage: "Request was cancelled",
                exception: tcEx,
                duration: stopwatch.Elapsed,
                serviceName: serviceName,
                endpoint: endpoint,
                statusCode: 0);
        }
        catch (HttpRequestException httpEx)
        {
            stopwatch.Stop();
            return RawApiResult.Failure(
                errorMessage: $"HTTP request failed: {httpEx.Message}",
                exception: httpEx,
                duration: stopwatch.Elapsed,
                serviceName: serviceName,
                endpoint: endpoint,
                statusCode: (int?)httpEx.StatusCode ?? 502); // Bad Gateway
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return RawApiResult.Failure(
                errorMessage: $"Unexpected error: {ex.Message}",
                exception: ex,
                duration: stopwatch.Elapsed,
                serviceName: serviceName,
                endpoint: endpoint,
                statusCode: 500);
        }
    }

    /// <inheritdoc />
    public async Task<PreboundApiResult> ExecutePreboundApiAsync(
        PreboundApiDefinition api,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(context);

        // Perform template substitution
        string substitutedPayload;
        try
        {
            substitutedPayload = TemplateSubstitutor.Substitute(api.PayloadTemplate, context);
        }
        catch (TemplateSubstitutionException ex)
        {
            return PreboundApiResult.SubstitutionFailed(api, ex.Message);
        }
        catch (Exception ex)
        {
            return PreboundApiResult.SubstitutionFailed(api, $"Unexpected error during substitution: {ex.Message}");
        }

        // Execute the raw API call
        var result = await ExecuteRawApiAsync(
            api.ServiceName,
            api.Endpoint,
            substitutedPayload,
            HttpMethod.Post, // Prebound APIs are always POST per Bannou design
            ct);

        return PreboundApiResult.Success(api, substitutedPayload, result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreboundApiResult>> ExecutePreboundApiBatchAsync(
        IEnumerable<PreboundApiDefinition> apis,
        IReadOnlyDictionary<string, object?> context,
        BatchExecutionMode mode = BatchExecutionMode.Parallel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apis);
        ArgumentNullException.ThrowIfNull(context);

        var apiList = apis.ToList();
        if (apiList.Count == 0)
        {
            return Array.Empty<PreboundApiResult>();
        }

        return mode switch
        {
            BatchExecutionMode.Parallel => await ExecuteBatchParallelAsync(apiList, context, ct),
            BatchExecutionMode.Sequential => await ExecuteBatchSequentialAsync(apiList, context, stopOnFailure: false, ct),
            BatchExecutionMode.SequentialStopOnFailure => await ExecuteBatchSequentialAsync(apiList, context, stopOnFailure: true, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown batch execution mode")
        };
    }

    /// <summary>
    /// Executes APIs in parallel.
    /// </summary>
    private async Task<IReadOnlyList<PreboundApiResult>> ExecuteBatchParallelAsync(
        List<PreboundApiDefinition> apis,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct)
    {
        var tasks = apis.Select(api => ExecutePreboundApiAsync(api, context, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// Executes APIs sequentially.
    /// </summary>
    private async Task<IReadOnlyList<PreboundApiResult>> ExecuteBatchSequentialAsync(
        List<PreboundApiDefinition> apis,
        IReadOnlyDictionary<string, object?> context,
        bool stopOnFailure,
        CancellationToken ct)
    {
        var results = new List<PreboundApiResult>(apis.Count);

        foreach (var api in apis)
        {
            ct.ThrowIfCancellationRequested();

            var result = await ExecutePreboundApiAsync(api, context, ct);
            results.Add(result);

            if (stopOnFailure && !result.IsSuccess)
            {
                // Add placeholder results for remaining APIs
                var remainingApis = apis.Skip(results.Count);
                foreach (var remainingApi in remainingApis)
                {
                    results.Add(PreboundApiResult.SubstitutionFailed(
                        remainingApi,
                        $"Skipped due to previous failure in {api.ServiceName}{api.Endpoint}"));
                }
                break;
            }
        }

        return results;
    }
}
