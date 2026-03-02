using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Interface for Bannou WebSocket clients.
/// Enables mocking for unit tests of code that depends on BannouClient.
/// </summary>
public interface IBannouClient : IAsyncDisposable
{
    /// <summary>
    /// Whether the WebSocket connection is currently open.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Session ID assigned by the server after connection.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// All available API endpoints with their client-salted GUIDs.
    /// Key format: "/path" (e.g., "/species/get")
    /// </summary>
    IReadOnlyDictionary<string, Guid> AvailableApis { get; }

    /// <summary>
    /// Event raised when new capabilities are added to the session.
    /// Fires once per capability manifest update with all newly added capabilities.
    /// </summary>
    event Action<IReadOnlyList<ClientCapabilityEntry>>? OnCapabilitiesAdded;

    /// <summary>
    /// Event raised when capabilities are removed from the session.
    /// Fires once per capability manifest update with all removed capabilities.
    /// </summary>
    event Action<IReadOnlyList<ClientCapabilityEntry>>? OnCapabilitiesRemoved;

    /// <summary>
    /// Current access token (JWT).
    /// </summary>
    string? AccessToken { get; }

    /// <summary>
    /// Current refresh token for re-authentication.
    /// </summary>
    string? RefreshToken { get; }

    /// <summary>
    /// Last error message from a failed operation.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Connects to a Bannou server using username/password authentication.
    /// </summary>
    /// <param name="serverUrl">Base URL (e.g., "http://localhost:8080" or "https://game.example.com")</param>
    /// <param name="email">Account email</param>
    /// <param name="password">Account password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    Task<bool> ConnectAsync(
        string serverUrl,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects using an existing JWT token.
    /// </summary>
    /// <param name="serverUrl">Base URL (e.g., "http://localhost:8080")</param>
    /// <param name="accessToken">Valid JWT access token</param>
    /// <param name="refreshToken">Optional refresh token for re-authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    Task<bool> ConnectWithTokenAsync(
        string serverUrl,
        string accessToken,
        string? refreshToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects in internal mode using a service token (or network-trust if token is null) without JWT login.
    /// </summary>
    /// <param name="connectUrl">Full WebSocket URL to the Connect service (internal node).</param>
    /// <param name="serviceToken">Optional X-Service-Token for internal auth mode.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    Task<bool> ConnectInternalAsync(
        string connectUrl,
        string? serviceToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new account and connects.
    /// </summary>
    /// <param name="serverUrl">Base URL</param>
    /// <param name="username">Desired username</param>
    /// <param name="email">Email address</param>
    /// <param name="password">Password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if registration and connection successful</returns>
    Task<bool> RegisterAndConnectAsync(
        string serverUrl,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the service GUID for a specific API endpoint.
    /// </summary>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <returns>The client-salted GUID, or null if not found</returns>
    Guid? GetServiceGuid(string endpoint);

    /// <summary>
    /// Invokes a service method by specifying the API endpoint path.
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <typeparam name="TResponse">Response model type</typeparam>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering (default 0)</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ApiResponse containing either the success result or error details</returns>
    Task<ApiResponse<TResponse>> InvokeAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        ushort channel = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a fire-and-forget event (no response expected).
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <param name="endpoint">API path</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendEventAsync<TRequest>(
        string endpoint,
        TRequest request,
        ushort channel = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a handler for server-pushed events.
    /// </summary>
    /// <param name="eventType">Event type to handle (e.g., "capability-manifest")</param>
    /// <param name="handler">Handler function receiving the JSON payload</param>
    void OnEvent(string eventType, Action<string> handler);

    /// <summary>
    /// Removes an event handler.
    /// </summary>
    /// <param name="eventType">Event type to unregister</param>
    void RemoveEventHandler(string eventType);

    /// <summary>
    /// Subscribe to a typed event with automatic deserialization.
    /// The event type must be a generated client event class inheriting from <see cref="BaseClientEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">Event type to subscribe to (e.g., ChatMessageReceivedClientEvent)</typeparam>
    /// <param name="handler">Handler to invoke when event is received, with the deserialized event object</param>
    /// <returns>Subscription handle - call <see cref="IDisposable.Dispose"/> to unsubscribe</returns>
    /// <exception cref="ArgumentException">Thrown if TEvent is not a registered client event type</exception>
    IEventSubscription OnEvent<TEvent>(Action<TEvent> handler) where TEvent : BaseClientEvent;

    /// <summary>
    /// Remove all typed handlers for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">Event type to remove all handlers for</typeparam>
    void RemoveEventHandlers<TEvent>() where TEvent : BaseClientEvent;

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
