using BeyondImmersion.BannouService.Account;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Service responsible for OAuth provider integrations.
/// Handles code exchange, user info retrieval, and account linking for Discord, Google, Twitch, and Steam.
/// </summary>
public interface IOAuthProviderService
{
    /// <summary>
    /// Exchanges a Discord authorization code for user information.
    /// </summary>
    /// <param name="code">The authorization code from Discord.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OAuth user info if successful, null otherwise.</returns>
    Task<OAuthUserInfo?> ExchangeDiscordCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a Google authorization code for user information.
    /// </summary>
    /// <param name="code">The authorization code from Google.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OAuth user info if successful, null otherwise.</returns>
    Task<OAuthUserInfo?> ExchangeGoogleCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a Twitch authorization code for user information.
    /// </summary>
    /// <param name="code">The authorization code from Twitch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OAuth user info if successful, null otherwise.</returns>
    Task<OAuthUserInfo?> ExchangeTwitchCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a Steam Session Ticket and retrieves the Steam ID.
    /// </summary>
    /// <param name="ticket">The Steam session ticket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Steam ID if valid, null otherwise.</returns>
    Task<string?> ValidateSteamTicketAsync(string ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds or creates an account linked to an OAuth provider identity.
    /// </summary>
    /// <param name="provider">The OAuth provider.</param>
    /// <param name="userInfo">The user info from the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The linked account if successful, null otherwise.</returns>
    Task<AccountResponse?> FindOrCreateOAuthAccountAsync(Provider provider, OAuthUserInfo userInfo, CancellationToken cancellationToken);

    /// <summary>
    /// Generates the OAuth authorization URL for a provider.
    /// </summary>
    /// <param name="provider">The OAuth provider.</param>
    /// <param name="redirectUri">The redirect URI for the callback.</param>
    /// <param name="state">Optional state parameter for CSRF protection.</param>
    /// <returns>The authorization URL, or null if provider is not configured.</returns>
    string? GetAuthorizationUrl(Provider provider, string? redirectUri, string? state);

    /// <summary>
    /// Checks if mock OAuth is enabled (for testing).
    /// </summary>
    bool IsMockEnabled { get; }

    /// <summary>
    /// Handles mock OAuth authentication (for testing).
    /// </summary>
    /// <param name="provider">The OAuth provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mock OAuth user info.</returns>
    Task<OAuthUserInfo> GetMockUserInfoAsync(Provider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets mock Steam user info (for testing).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mock Steam user info.</returns>
    Task<OAuthUserInfo> GetMockSteamUserInfoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// User info retrieved from OAuth provider.
/// </summary>
public class OAuthUserInfo
{
    /// <summary>
    /// The unique user ID from the provider.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The user's email address (may be null for some providers like Steam).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The user's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The user's avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }
}
