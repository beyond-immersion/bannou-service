namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Extension to the generated IPermissionService interface for session connection tracking.
/// These methods are used internally by the event handlers and are not part of the public API.
/// </summary>
public partial interface IPermissionService
{
    /// <summary>
    /// Handles a session connection event from the Connect service.
    /// Adds the session to activeConnections and triggers initial capability delivery.
    /// Roles and authorizations from the event are used to compile capabilities without API calls.
    /// </summary>
    /// <param name="sessionId">The session ID that connected.</param>
    /// <param name="accountId">The account ID owning the session.</param>
    /// <param name="roles">User roles from JWT (e.g., ["user", "admin"]).</param>
    /// <param name="authorizations">Authorization states from JWT (e.g., ["arcadia:authorized"]).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code indicating success or failure.</returns>
    Task<(StatusCodes, SessionUpdateResponse?)> HandleSessionConnectedAsync(
        string sessionId,
        string accountId,
        ICollection<string>? roles,
        ICollection<string>? authorizations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a session disconnection event from the Connect service.
    /// Removes the session from activeConnections to prevent publishing to non-existent exchanges.
    /// </summary>
    /// <param name="sessionId">The session ID that disconnected.</param>
    /// <param name="reconnectable">Whether the session can reconnect within the window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code indicating success or failure.</returns>
    Task<(StatusCodes, SessionUpdateResponse?)> HandleSessionDisconnectedAsync(
        string sessionId,
        bool reconnectable,
        CancellationToken cancellationToken = default);
}
