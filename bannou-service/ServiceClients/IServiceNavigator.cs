using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Aggregates all service clients into a single navigation interface with
/// session context awareness for pushing client events.
/// </summary>
/// <remarks>
/// <para>
/// This interface consolidates service client access (e.g., navigator.Account, navigator.Asset)
/// and provides session context methods for:
/// 1. Determining if the current request originated from a WebSocket client
/// 2. Getting the originating session ID for client event delivery
/// 3. Publishing events directly back to the requester
/// </para>
///
/// <para>
/// Usage example:
/// <code>
/// public class AssetService : IAssetService
/// {
///     private readonly IServiceNavigator _nav;
///
///     public AssetService(IServiceNavigator nav) => _nav = nav;
///
///     public async Task&lt;(StatusCodes, CreateMetabundleResponse?)&gt; CreateMetabundleAsync(...)
///     {
///         // For long-running jobs, capture the session for later notification
///         var sessionId = _nav.GetRequesterSessionId();
///
///         // Start async job...
///         var job = new MetabundleJob { OriginatingSessionId = sessionId };
///
///         // When job completes, notify the client:
///         if (_nav.HasClientContext)
///         {
///             await _nav.PublishToRequesterAsync(new MetabundleCompleteEvent { JobId = job.JobId });
///         }
///     }
/// }
/// </code>
/// </para>
///
/// <para>
/// WARNING: Session ID is for correlation, tracing, and client event delivery only.
/// DO NOT use for authentication, authorization, or ownership decisions.
/// </para>
/// </remarks>
public partial interface IServiceNavigator
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Context Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the session ID of the WebSocket client that initiated this request.
    /// Returns null if the request did not originate from a WebSocket client.
    /// </summary>
    /// <returns>Session ID string, or null if not a client-initiated request.</returns>
    string? GetRequesterSessionId();

    /// <summary>
    /// Gets the correlation ID for distributed tracing.
    /// Returns null if no correlation ID was provided.
    /// </summary>
    /// <returns>Correlation ID string, or null.</returns>
    string? GetCorrelationId();

    /// <summary>
    /// Returns true if this request originated from a WebSocket client,
    /// meaning GetRequesterSessionId() will return a valid session ID.
    /// </summary>
    bool HasClientContext { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Client Event Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Publishes an event to the WebSocket session that initiated this request.
    /// Returns false if there is no client context (request was not from WebSocket).
    /// </summary>
    /// <typeparam name="TEvent">The event type (must extend BaseClientEvent).</typeparam>
    /// <param name="eventData">The event data to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published, false if no client context exists.</returns>
    Task<bool> PublishToRequesterAsync<TEvent>(
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent;

    /// <summary>
    /// Publishes an event to a specific session by ID.
    /// Use this when you have stored a session ID for later notification.
    /// </summary>
    /// <typeparam name="TEvent">The event type (must extend BaseClientEvent).</typeparam>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="eventData">The event data to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> PublishToSessionAsync<TEvent>(
        string sessionId,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent;

    // ═══════════════════════════════════════════════════════════════════════════
    // Raw API Execution Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes a raw JSON payload against a service endpoint.
    /// This is the low-level method used by both Connect shortcuts and Contract prebound APIs.
    /// </summary>
    /// <param name="serviceName">Target service name (e.g., "currency", "inventory").</param>
    /// <param name="endpoint">Endpoint path (e.g., "/currency/transfer").</param>
    /// <param name="jsonPayload">Raw JSON payload string to send.</param>
    /// <param name="method">HTTP method (default: POST).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw API result with status, response body, and headers.</returns>
    Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        string jsonPayload,
        HttpMethod? method = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a raw byte payload against a service endpoint.
    /// Used by Connect for zero-copy shortcut forwarding.
    /// </summary>
    /// <param name="serviceName">Target service name.</param>
    /// <param name="endpoint">Endpoint path.</param>
    /// <param name="payload">Raw byte payload to send.</param>
    /// <param name="method">HTTP method (default: POST).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw API result with status, response body, and headers.</returns>
    Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        ReadOnlyMemory<byte> payload,
        HttpMethod? method = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a prebound API with variable substitution.
    /// </summary>
    /// <param name="api">Prebound API definition with template.</param>
    /// <param name="context">Variables to substitute into the template.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Prebound API execution result including substituted payload.</returns>
    Task<PreboundApiResult> ExecutePreboundApiAsync(
        PreboundApiDefinition api,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default);

    /// <summary>
    /// Executes multiple prebound APIs in batch.
    /// </summary>
    /// <param name="apis">Collection of prebound API definitions to execute.</param>
    /// <param name="context">Variables to substitute into templates.</param>
    /// <param name="mode">How to execute the batch (parallel, sequential, stop on failure).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of results for each API in order.</returns>
    Task<IReadOnlyList<PreboundApiResult>> ExecutePreboundApiBatchAsync(
        IEnumerable<PreboundApiDefinition> apis,
        IReadOnlyDictionary<string, object?> context,
        BatchExecutionMode mode = BatchExecutionMode.Parallel,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // Service Client Properties (Generated)
    // ═══════════════════════════════════════════════════════════════════════════
    // The following properties are generated by scripts/generate-service-navigator.py
    // and added via partial interface in Generated/IServiceNavigator.cs
}
