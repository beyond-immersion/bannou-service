namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Processes raw platform events into sentiment categories and intensity values.
/// Manages tracked viewer mapping in Redis for anonymous sentiment attribution.
/// </summary>
public interface ISentimentProcessor
{
    /// <summary>
    /// Classifies text into a sentiment category and intensity.
    /// Stateless computation — no side effects, no state access.
    /// </summary>
    /// <param name="text">The text to classify.</param>
    /// <returns>Sentiment category and intensity (0.0 to 1.0).</returns>
    Task<(SentimentCategory Category, float Intensity)> ClassifyAsync(string text);

    /// <summary>
    /// Processes a chat message from a platform webhook into a buffered sentiment entry.
    /// Writes to the sentiment buffer store with session-scoped TTL.
    /// </summary>
    /// <param name="platformSessionId">The platform session this message belongs to.</param>
    /// <param name="messageText">The chat message text.</param>
    /// <param name="senderId">Platform-specific sender identifier (hashed for privacy).</param>
    /// <param name="senderBadges">Platform badges/roles for viewer type classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessChatMessageAsync(Guid platformSessionId, string messageText, string senderId, string? senderBadges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a subscription/raid/donation event into a buffered sentiment entry.
    /// </summary>
    /// <param name="platformSessionId">The platform session.</param>
    /// <param name="eventData">Platform-specific event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessSubscriptionEventAsync(Guid platformSessionId, object eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a super chat / monetary event into a buffered sentiment entry.
    /// </summary>
    /// <param name="platformSessionId">The platform session.</param>
    /// <param name="amount">Monetary amount.</param>
    /// <param name="senderId">Platform-specific sender identifier (hashed for privacy).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessSuperChatAsync(Guid platformSessionId, decimal amount, string senderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a generic webhook payload for custom platforms.
    /// </summary>
    /// <param name="platformSessionId">The platform session.</param>
    /// <param name="payload">The raw webhook payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessGenericWebhookAsync(Guid platformSessionId, object payload, CancellationToken cancellationToken = default);
}
