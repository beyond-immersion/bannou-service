namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Platform-specific webhook HMAC/token validation and event routing.
/// Performs signature validation at the controller level before model binding.
/// </summary>
public interface IPlatformWebhookHandler
{
    /// <summary>
    /// Validates a Twitch EventSub webhook signature (HMAC-SHA256).
    /// </summary>
    /// <param name="signature">The Twitch-Eventsub-Message-Signature header value.</param>
    /// <param name="messageId">The Twitch-Eventsub-Message-Id header value.</param>
    /// <param name="timestamp">The Twitch-Eventsub-Message-Timestamp header value.</param>
    /// <param name="body">The raw request body for HMAC computation.</param>
    /// <returns>True if the signature is valid.</returns>
    bool ValidateTwitchSignature(string signature, string messageId, string timestamp, string body);

    /// <summary>
    /// Validates a YouTube webhook verification token.
    /// </summary>
    /// <param name="token">The verification token from the request.</param>
    /// <returns>True if the token matches the configured secret.</returns>
    bool ValidateYouTubeToken(string token);

    /// <summary>
    /// Validates a custom platform webhook signature (configurable HMAC).
    /// </summary>
    /// <param name="signature">The signature header value.</param>
    /// <param name="body">The raw request body for HMAC computation.</param>
    /// <returns>True if the signature is valid.</returns>
    bool ValidateCustomSignature(string signature, string body);
}
