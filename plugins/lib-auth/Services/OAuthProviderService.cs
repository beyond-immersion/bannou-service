using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Implementation of OAuth provider integrations.
/// Handles code exchange, user info retrieval, and account linking for Discord, Google, Twitch, and Steam.
/// </summary>
public class OAuthProviderService : IOAuthProviderService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IAccountClient _accountClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OAuthProviderService> _logger;

    private const string REDIS_STATE_STORE = "auth-statestore";
    private const string DISCORD_TOKEN_URL = "https://discord.com/api/oauth2/token";
    private const string DISCORD_USER_URL = "https://discord.com/api/users/@me";
    private const string GOOGLE_TOKEN_URL = "https://oauth2.googleapis.com/token";
    private const string GOOGLE_USER_URL = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string TWITCH_TOKEN_URL = "https://id.twitch.tv/oauth2/token";
    private const string TWITCH_USER_URL = "https://api.twitch.tv/helix/users";
    private const string STEAM_AUTH_URL = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

    /// <summary>
    /// Initializes a new instance of OAuthProviderService.
    /// </summary>
    public OAuthProviderService(
        IStateStoreFactory stateStoreFactory,
        IAccountClient accountClient,
        IHttpClientFactory httpClientFactory,
        AuthServiceConfiguration configuration,
        IMessageBus messageBus,
        ILogger<OAuthProviderService> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _accountClient = accountClient ?? throw new ArgumentNullException(nameof(accountClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsMockEnabled => _configuration.MockProviders;

    /// <inheritdoc/>
    public async Task<OAuthUserInfo?> ExchangeDiscordCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var discordClientId = _configuration.DiscordClientId;
        var discordClientSecret = _configuration.DiscordClientSecret;
        var discordRedirectUri = GetEffectiveRedirectUri(_configuration.DiscordRedirectUri, "discord");

        if (discordClientId == null || discordClientSecret == null || discordRedirectUri == null)
        {
            _logger.LogError("Discord OAuth not configured - set AUTH_DISCORD_CLIENT_ID, AUTH_DISCORD_CLIENT_SECRET, or set BANNOU_SERVICE_DOMAIN");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", discordClientId },
                { "client_secret", discordClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", discordRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(DISCORD_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Discord token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<DiscordTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Discord token response missing access_token");
                return null;
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, DISCORD_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Discord user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var userData = BannouJson.Deserialize<DiscordUserResponse>(userJson);
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Discord user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.GlobalName ?? userData.Username,
                AvatarUrl = userData.Avatar != null ? $"https://cdn.discordapp.com/avatars/{userData.Id}/{userData.Avatar}.png" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Discord authorization code");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ExchangeDiscordCode",
                ex.GetType().Name,
                ex.Message,
                dependency: "http:discord",
                endpoint: "post:/auth/oauth/discord/callback",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OAuthUserInfo?> ExchangeGoogleCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var googleClientId = _configuration.GoogleClientId;
        var googleClientSecret = _configuration.GoogleClientSecret;
        var googleRedirectUri = GetEffectiveRedirectUri(_configuration.GoogleRedirectUri, "google");

        if (googleClientId == null || googleClientSecret == null || googleRedirectUri == null)
        {
            _logger.LogError("Google OAuth not configured - set AUTH_GOOGLE_CLIENT_ID, AUTH_GOOGLE_CLIENT_SECRET, or set BANNOU_SERVICE_DOMAIN");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", googleClientId },
                { "client_secret", googleClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", googleRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(GOOGLE_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Google token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<GoogleTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Google token response missing access_token");
                return null;
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, GOOGLE_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Google user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var userData = BannouJson.Deserialize<GoogleUserResponse>(userJson);
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Google user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.Name,
                AvatarUrl = userData.Picture
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Google authorization code");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ExchangeGoogleCode",
                ex.GetType().Name,
                ex.Message,
                dependency: "http:google",
                endpoint: "post:/auth/oauth/google/callback",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OAuthUserInfo?> ExchangeTwitchCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var twitchClientId = _configuration.TwitchClientId;
        var twitchClientSecret = _configuration.TwitchClientSecret;
        var twitchRedirectUri = GetEffectiveRedirectUri(_configuration.TwitchRedirectUri, "twitch");

        if (twitchClientId == null || twitchClientSecret == null || twitchRedirectUri == null)
        {
            _logger.LogError("Twitch OAuth not configured - set AUTH_TWITCH_CLIENT_ID, AUTH_TWITCH_CLIENT_SECRET, or set BANNOU_SERVICE_DOMAIN");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", twitchClientId },
                { "client_secret", twitchClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", twitchRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(TWITCH_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<TwitchTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Twitch token response missing access_token");
                return null;
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, TWITCH_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            userRequest.Headers.Add("Client-Id", _configuration.TwitchClientId);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var usersData = BannouJson.Deserialize<TwitchUsersResponse>(userJson);
            var userData = usersData?.Data?.FirstOrDefault();
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Twitch user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.DisplayName,
                AvatarUrl = userData.ProfileImageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Twitch authorization code");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ExchangeTwitchCode",
                ex.GetType().Name,
                ex.Message,
                dependency: "http:twitch",
                endpoint: "post:/auth/oauth/twitch/callback",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> ValidateSteamTicketAsync(string ticket, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_configuration.SteamApiKey) ||
                string.IsNullOrWhiteSpace(_configuration.SteamAppId))
            {
                _logger.LogError("Steam API Key or App ID not configured");
                return null;
            }

            using var httpClient = _httpClientFactory.CreateClient();

            var requestUrl = $"{STEAM_AUTH_URL}?key={_configuration.SteamApiKey}&appid={_configuration.SteamAppId}&ticket={ticket}";
            var response = await httpClient.GetAsync(requestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Steam ticket validation failed: {Status} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var steamResponse = BannouJson.Deserialize<SteamAuthResponse>(responseJson);

            if (steamResponse?.Response?.Error != null)
            {
                _logger.LogWarning("Steam API returned error: {ErrorCode} - {ErrorDesc}",
                    steamResponse.Response.Error.ErrorCode, steamResponse.Response.Error.ErrorDesc);
                return null;
            }

            if (steamResponse?.Response?.Params == null || steamResponse.Response.Params.Result != "OK")
            {
                _logger.LogWarning("Steam ticket validation returned unexpected result");
                return null;
            }

            if (steamResponse.Response.Params.VacBanned || steamResponse.Response.Params.PublisherBanned)
            {
                _logger.LogWarning("Steam user is banned: VacBanned={VacBanned}, PublisherBanned={PublisherBanned}",
                    steamResponse.Response.Params.VacBanned, steamResponse.Response.Params.PublisherBanned);
                return null;
            }

            return steamResponse.Response.Params.SteamId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Steam ticket");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ValidateSteamTicket",
                ex.GetType().Name,
                ex.Message,
                dependency: "http:steam",
                endpoint: "post:/auth/steam/callback",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<AccountResponse?> FindOrCreateOAuthAccountAsync(Provider provider, OAuthUserInfo userInfo, CancellationToken cancellationToken, string? providerOverride = null)
    {
        // Handle null userInfo gracefully - return null if no user info provided
        if (userInfo == null)
        {
            _logger.LogWarning("FindOrCreateOAuthAccountAsync called with null userInfo for provider {Provider}", provider);
            return null;
        }

        var providerName = providerOverride ?? provider.ToString().ToLower();
        var oauthLinkKey = $"oauth-link:{providerName}:{userInfo.ProviderId}";

        try
        {
            var linkStore = _stateStoreFactory.GetStore<string>(REDIS_STATE_STORE);

            // Check existing link (stored as string since Guid is a value type)
            var existingAccountIdStr = await linkStore.GetAsync(oauthLinkKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingAccountIdStr) && Guid.TryParse(existingAccountIdStr, out var existingAccountId) && existingAccountId != Guid.Empty)
            {
                try
                {
                    var account = await _accountClient.GetAccountAsync(
                        new GetAccountRequest { AccountId = existingAccountId },
                        cancellationToken);
                    _logger.LogInformation("Found existing account {AccountId} for {Provider} user {ProviderId}",
                        account.AccountId, providerName, userInfo.ProviderId);
                    return account;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogWarning("Linked account {AccountId} not found, removing stale OAuth link", existingAccountId);
                    await linkStore.DeleteAsync(oauthLinkKey, cancellationToken);
                }
            }

            // Create new account
            var createRequest = new CreateAccountRequest
            {
                Email = userInfo.Email ?? $"{providerName}_{userInfo.ProviderId}@oauth.local",
                DisplayName = userInfo.DisplayName ?? $"{providerName}_user",
                EmailVerified = userInfo.Email != null,
                PasswordHash = null
            };

            AccountResponse? newAccount;
            try
            {
                newAccount = await _accountClient.CreateAccountAsync(createRequest, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                if (!string.IsNullOrEmpty(userInfo.Email))
                {
                    try
                    {
                        newAccount = await _accountClient.GetAccountByEmailAsync(
                            new GetAccountByEmailRequest { Email = userInfo.Email },
                            cancellationToken);
                        _logger.LogInformation("Found existing account by email {Email} for {Provider} user",
                            userInfo.Email, providerName);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Email conflict but couldn't find account: {Email}", userInfo.Email);
                        await _messageBus.TryPublishErrorAsync(
                            "auth",
                            "FindOrCreateOAuthAccount",
                            innerEx.GetType().Name,
                            innerEx.Message,
                            dependency: "account",
                            stack: innerEx.StackTrace,
                            cancellationToken: cancellationToken);
                        return null;
                    }
                }
                else
                {
                    _logger.LogError("Account creation conflict but no email to search by");
                    return null;
                }
            }

            if (newAccount == null)
            {
                _logger.LogError("Failed to create account for {Provider} user {ProviderId}", providerName, userInfo.ProviderId);
                return null;
            }

            // Store the OAuth link (as string since Guid is a value type)
            await linkStore.SaveAsync(
                oauthLinkKey,
                newAccount.AccountId.ToString(),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created new account {AccountId} and linked to {Provider} user {ProviderId}",
                newAccount.AccountId, providerName, userInfo.ProviderId);

            return newAccount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding or creating OAuth account for {Provider} user {ProviderId}",
                providerName, userInfo.ProviderId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "FindOrCreateOAuthAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: "account",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public string? GetAuthorizationUrl(Provider provider, string? redirectUri, string? state)
    {
        var encodedState = HttpUtility.UrlEncode(state ?? Guid.NewGuid().ToString());

        switch (provider)
        {
            case Provider.Discord:
                if (string.IsNullOrWhiteSpace(_configuration.DiscordClientId))
                {
                    _logger.LogError("Discord Client ID not configured");
                    return null;
                }
                var effectiveDiscordRedirect = redirectUri ?? GetEffectiveRedirectUri(_configuration.DiscordRedirectUri, "discord");
                if (effectiveDiscordRedirect == null)
                {
                    _logger.LogError("Discord redirect URI not configured - set AUTH_DISCORD_REDIRECT_URI or BANNOU_SERVICE_DOMAIN");
                    return null;
                }
                var discordRedirectUri = HttpUtility.UrlEncode(effectiveDiscordRedirect);
                return $"https://discord.com/oauth2/authorize?client_id={_configuration.DiscordClientId}&response_type=code&redirect_uri={discordRedirectUri}&scope=identify%20email&state={encodedState}";

            case Provider.Google:
                if (string.IsNullOrWhiteSpace(_configuration.GoogleClientId))
                {
                    _logger.LogError("Google Client ID not configured");
                    return null;
                }
                var effectiveGoogleRedirect = redirectUri ?? GetEffectiveRedirectUri(_configuration.GoogleRedirectUri, "google");
                if (effectiveGoogleRedirect == null)
                {
                    _logger.LogError("Google redirect URI not configured - set AUTH_GOOGLE_REDIRECT_URI or BANNOU_SERVICE_DOMAIN");
                    return null;
                }
                var googleRedirectUri = HttpUtility.UrlEncode(effectiveGoogleRedirect);
                return $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_configuration.GoogleClientId}&response_type=code&redirect_uri={googleRedirectUri}&scope=openid%20email%20profile&state={encodedState}";

            case Provider.Twitch:
                if (string.IsNullOrWhiteSpace(_configuration.TwitchClientId))
                {
                    _logger.LogError("Twitch Client ID not configured");
                    return null;
                }
                var effectiveTwitchRedirect = redirectUri ?? GetEffectiveRedirectUri(_configuration.TwitchRedirectUri, "twitch");
                if (effectiveTwitchRedirect == null)
                {
                    _logger.LogError("Twitch redirect URI not configured - set AUTH_TWITCH_REDIRECT_URI or BANNOU_SERVICE_DOMAIN");
                    return null;
                }
                var twitchRedirectUri = HttpUtility.UrlEncode(effectiveTwitchRedirect);
                return $"https://id.twitch.tv/oauth2/authorize?client_id={_configuration.TwitchClientId}&response_type=code&redirect_uri={twitchRedirectUri}&scope=user:read:email&state={encodedState}";

            default:
                _logger.LogWarning("Unknown OAuth provider: {Provider}", provider);
                return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OAuthUserInfo> GetMockUserInfoAsync(Provider provider, CancellationToken cancellationToken = default)
    {
        var mockProviderId = provider switch
        {
            Provider.Discord => _configuration.MockDiscordId,
            Provider.Google => _configuration.MockGoogleId,
            Provider.Twitch => "mock-twitch-user-id-12345",
            _ => Guid.NewGuid().ToString()
        };

        await Task.CompletedTask;
        return new OAuthUserInfo
        {
            ProviderId = mockProviderId,
            Email = $"mock-{provider.ToString().ToLower()}@test.local",
            DisplayName = $"Mock {provider} User"
        };
    }

    /// <inheritdoc/>
    public async Task<OAuthUserInfo> GetMockSteamUserInfoAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new OAuthUserInfo
        {
            ProviderId = _configuration.MockSteamId,
            Email = null,
            DisplayName = $"Steam_{_configuration.MockSteamId.Substring(_configuration.MockSteamId.Length - 6)}"
        };
    }

    /// <summary>
    /// Gets the effective redirect URI for an OAuth provider.
    /// Uses explicit config if set, otherwise derives from ServiceDomain if available.
    /// </summary>
    /// <param name="configuredUri">The explicitly configured redirect URI (nullable)</param>
    /// <param name="provider">The provider name (discord, google, twitch)</param>
    /// <returns>The effective redirect URI, or null if neither is configured</returns>
    private static string? GetEffectiveRedirectUri(string? configuredUri, string provider)
    {
        // If explicit redirect URI is configured, use it
        if (!string.IsNullOrWhiteSpace(configuredUri))
        {
            return configuredUri;
        }

        // If ServiceDomain is configured, derive the redirect URI from it
        var serviceDomain = Program.Configuration?.ServiceDomain;
        if (!string.IsNullOrWhiteSpace(serviceDomain))
        {
            return $"https://{serviceDomain}/auth/oauth/{provider}/callback";
        }

        // Neither configured - return null to indicate provider should be disabled
        return null;
    }

    #region OAuth Response Models

    private class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private class DiscordUserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }
    }

    private class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }

    private class GoogleUserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }

    private class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }

    private class TwitchUsersResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchUser>? Data { get; set; }
    }

    private class TwitchUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }
    }

    private class SteamAuthResponse
    {
        [JsonPropertyName("response")]
        public SteamAuthResponseData? Response { get; set; }
    }

    private class SteamAuthResponseData
    {
        [JsonPropertyName("params")]
        public SteamAuthParams? Params { get; set; }

        [JsonPropertyName("error")]
        public SteamAuthError? Error { get; set; }
    }

    private class SteamAuthParams
    {
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("ownersteamid")]
        public string OwnerSteamId { get; set; } = string.Empty;

        [JsonPropertyName("vacbanned")]
        public bool VacBanned { get; set; }

        [JsonPropertyName("publisherbanned")]
        public bool PublisherBanned { get; set; }
    }

    private class SteamAuthError
    {
        [JsonPropertyName("errorcode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("errordesc")]
        public string ErrorDesc { get; set; } = string.Empty;
    }

    #endregion
}
