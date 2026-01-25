using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Service responsible for session lifecycle management.
/// Handles session storage, retrieval, indexing, and invalidation.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Gets all active sessions for an account.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active session information.</returns>
    Task<List<SessionInfo>> GetAccountSessionsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a session key to the account's session index.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="sessionKey">The session key to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddSessionToAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a session key from the account's session index.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="sessionKey">The session key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveSessionFromAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a reverse index mapping session ID to session key.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="ttlSeconds">TTL for the index entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddSessionIdReverseIndexAsync(string sessionId, string sessionKey, int ttlSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the reverse index entry for a session ID.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveSessionIdReverseIndexAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a session key by session ID using the reverse index.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session key if found, null otherwise.</returns>
    Task<string?> FindSessionKeyBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session by its key.
    /// </summary>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteSessionAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session data by session key.
    /// </summary>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session data if found, null otherwise.</returns>
    Task<SessionDataModel?> GetSessionAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves session data.
    /// </summary>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="sessionData">The session data to save.</param>
    /// <param name="ttlSeconds">Optional TTL in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSessionAsync(string sessionKey, SessionDataModel sessionData, int? ttlSeconds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all session keys for an account from the index.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of session keys, or empty list if none found.</returns>
    Task<List<string>> GetSessionKeysForAccountAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the account sessions index.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAccountSessionsIndexAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all sessions for an account.
    /// Deletes all session data and publishes invalidation event.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="reason">The reason for invalidation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllSessionsForAccountAsync(Guid accountId, SessionInvalidatedEventReason reason = SessionInvalidatedEventReason.Account_deleted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a session invalidated event.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="sessionIds">List of invalidated session IDs.</param>
    /// <param name="reason">The reason for invalidation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishSessionInvalidatedEventAsync(Guid accountId, List<string> sessionIds, SessionInvalidatedEventReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a session updated event.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="roles">Updated roles.</param>
    /// <param name="authorizations">Updated authorizations.</param>
    /// <param name="reason">The reason for the update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishSessionUpdatedEventAsync(Guid accountId, string sessionId, List<string> roles, List<string> authorizations, SessionUpdatedEventReason reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal model for session data stored in Redis.
/// </summary>
public class SessionDataModel
{
    /// <summary>
    /// The account ID.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// The account email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The account display name. Null if user hasn't set one.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// List of roles assigned to this session.
    /// </summary>
    public List<string> Roles { get; set; } = new List<string>();

    /// <summary>
    /// List of authorizations for this session.
    /// </summary>
    public List<string> Authorizations { get; set; } = new List<string>();

    /// <summary>
    /// The unique session ID (displayed to users).
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Session creation time as Unix timestamp.
    /// Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long CreatedAtUnix { get; set; }

    /// <summary>
    /// Session expiration time as Unix timestamp.
    /// Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long ExpiresAtUnix { get; set; }

    /// <summary>
    /// Last activity time as Unix timestamp. Updated on each successful token validation.
    /// Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long LastActiveAtUnix { get; set; }

    /// <summary>
    /// Session creation time as DateTimeOffset.
    /// Computed property - not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Session expiration time as DateTimeOffset.
    /// Computed property - not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset ExpiresAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
        set => ExpiresAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Last activity time as DateTimeOffset. Updated on each successful token validation.
    /// Computed property - not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastActiveAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(LastActiveAtUnix);
        set => LastActiveAtUnix = value.ToUnixTimeSeconds();
    }
}
