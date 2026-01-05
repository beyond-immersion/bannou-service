namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Interface for session management operations.
/// Supports distributed WebSocket connection state across multiple instances.
/// </summary>
public interface ISessionManager
{
    #region Service Mappings

    /// <summary>
    /// Stores session service mappings with TTL.
    /// Maps service names to client-salted GUIDs for this session.
    /// </summary>
    Task SetSessionServiceMappingsAsync(
        string sessionId,
        Dictionary<string, Guid> serviceMappings,
        TimeSpan? ttl = null);

    /// <summary>
    /// Retrieves session service mappings.
    /// </summary>
    Task<Dictionary<string, Guid>?> GetSessionServiceMappingsAsync(string sessionId);

    #endregion

    #region Connection State

    /// <summary>
    /// Stores connection state in distributed storage.
    /// </summary>
    Task SetConnectionStateAsync(
        string sessionId,
        ConnectionStateData stateData,
        TimeSpan? ttl = null);

    /// <summary>
    /// Retrieves connection state from distributed storage.
    /// </summary>
    Task<ConnectionStateData?> GetConnectionStateAsync(string sessionId);

    #endregion

    #region Heartbeat

    /// <summary>
    /// Updates session heartbeat timestamp.
    /// Used for connection liveness tracking across distributed instances.
    /// </summary>
    Task UpdateSessionHeartbeatAsync(string sessionId, string instanceId);

    #endregion

    #region Reconnection Support

    /// <summary>
    /// Stores a reconnection token mapping to session ID.
    /// Token is stored for the duration of the reconnection window.
    /// </summary>
    Task SetReconnectionTokenAsync(
        string reconnectionToken,
        string sessionId,
        TimeSpan reconnectionWindow);

    /// <summary>
    /// Validates a reconnection token and returns the associated session ID.
    /// Returns null if token is invalid or expired.
    /// </summary>
    Task<string?> ValidateReconnectionTokenAsync(string reconnectionToken);

    /// <summary>
    /// Removes a reconnection token after successful reconnection or expiration.
    /// </summary>
    Task RemoveReconnectionTokenAsync(string reconnectionToken);

    /// <summary>
    /// Initiates reconnection window for a session.
    /// Preserves session data and service mappings for the reconnection window duration.
    /// </summary>
    Task InitiateReconnectionWindowAsync(
        string sessionId,
        string reconnectionToken,
        TimeSpan reconnectionWindow,
        ICollection<string>? userRoles);

    /// <summary>
    /// Restores a session from reconnection state.
    /// Clears reconnection fields and restores active session state.
    /// </summary>
    Task<ConnectionStateData?> RestoreSessionFromReconnectionAsync(
        string sessionId,
        string reconnectionToken);

    #endregion

    #region Session Cleanup

    /// <summary>
    /// Removes session data from distributed storage (cleanup on disconnect).
    /// </summary>
    Task RemoveSessionAsync(string sessionId);

    #endregion

    #region Session Events

    /// <summary>
    /// Publishes a session event for cross-instance communication.
    /// Used for disconnect notifications and other session lifecycle events.
    /// </summary>
    Task PublishSessionEventAsync(string eventType, string sessionId, object? eventData = null);

    #endregion

    #region Account Session Index

    /// <summary>
    /// Adds a session to the account's session index in distributed storage.
    /// Called when a session is connected to track all active sessions for an account.
    /// </summary>
    /// <param name="accountId">The account owning the session.</param>
    /// <param name="sessionId">The session ID to add.</param>
    Task AddSessionToAccountAsync(Guid accountId, string sessionId);

    /// <summary>
    /// Removes a session from the account's session index in distributed storage.
    /// Called when a session is disconnected.
    /// </summary>
    /// <param name="accountId">The account owning the session.</param>
    /// <param name="sessionId">The session ID to remove.</param>
    Task RemoveSessionFromAccountAsync(Guid accountId, string sessionId);

    /// <summary>
    /// Gets all active session IDs for an account from distributed storage.
    /// Returns an empty set if no sessions are found.
    /// </summary>
    /// <param name="accountId">The account to query.</param>
    /// <returns>Set of session IDs for the account.</returns>
    Task<HashSet<string>> GetSessionsForAccountAsync(Guid accountId);

    #endregion
}
