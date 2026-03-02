using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
    private readonly AppConfiguration _appConfiguration;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<OAuthProviderService> _logger;

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
        AppConfiguration appConfiguration,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider,
        ILogger<OAuthProviderService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _accountClient = accountClient;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsMockEnabled => _configuration.MockProviders;

    /// <inheritdoc/>
    public async Task<OAuthUserInfo?> ExchangeDiscordCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.ExchangeDiscordCode");
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

            using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
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
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.ExchangeGoogleCode");
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

            using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
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
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.ExchangeTwitchCode");
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

            using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
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
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.ValidateSteamTicket");
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
    public async Task<(AccountResponse? Account, bool IsNewAccount)> FindOrCreateOAuthAccountAsync(Provider provider, OAuthUserInfo userInfo, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.FindOrCreateOAuthAccount");
        // Handle null userInfo gracefully - return null if no user info provided
        if (userInfo == null)
        {
            _logger.LogWarning("FindOrCreateOAuthAccountAsync called with null userInfo for provider {Provider}", provider);
            return (null, false);
        }

        if (string.IsNullOrEmpty(userInfo.ProviderId))
        {
            _logger.LogError("OAuth user info missing ProviderId for provider {Provider}", provider);
            return (null, false);
        }

        var providerName = provider.ToString().ToLower();
        var oauthLinkKey = $"oauth-link:{providerName}:{userInfo.ProviderId}";

        try
        {
            var linkStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);

            // Check existing link (stored as string since Guid is a value type)
            var existingAccountIdStr = await linkStore.GetAsync(oauthLinkKey, cancellationToken);

            // Defensive: guard against corrupt Redis data (stored accountId should always be a valid non-empty GUID)
            if (!string.IsNullOrEmpty(existingAccountIdStr) && Guid.TryParse(existingAccountIdStr, out var existingAccountId) && existingAccountId != Guid.Empty)
            {
                try
                {
                    var account = await _accountClient.GetAccountAsync(
                        new GetAccountRequest { AccountId = existingAccountId },
                        cancellationToken);
                    _logger.LogInformation("Found existing account {AccountId} for {Provider} user {ProviderId}",
                        account.AccountId, providerName, userInfo.ProviderId);

                    // Ensure auth method is synced to Account service (idempotent)
                    await EnsureAuthMethodSyncedAsync(account.AccountId, provider, userInfo.ProviderId, cancellationToken);

                    return (account, false);
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogWarning("Linked account {AccountId} not found, cleaning up all stale OAuth links for account", existingAccountId);
                    await CleanupOAuthLinksForAccountAsync(existingAccountId, cancellationToken);
                }
            }

            // Create new account - email is null for providers that don't supply it (Steam, some OAuth)
            var createRequest = new CreateAccountRequest
            {
                Email = userInfo.Email, // Honest: null if provider doesn't supply email
                DisplayName = userInfo.DisplayName ?? $"{providerName}_user",
                EmailVerified = userInfo.Email != null, // Can only be verified if we have an email
                PasswordHash = null
            };

            AccountResponse? newAccount;
            var isNewAccount = false;
            try
            {
                newAccount = await _accountClient.CreateAccountAsync(createRequest, cancellationToken);
                isNewAccount = true;
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
                        _logger.LogInformation("Found existing account by email for {Provider} user: {AccountId}",
                            providerName, newAccount?.AccountId);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Email conflict but couldn't find account for {Provider} user", providerName);
                        await _messageBus.TryPublishErrorAsync(
                            "auth",
                            "FindOrCreateOAuthAccount",
                            innerEx.GetType().Name,
                            innerEx.Message,
                            dependency: "account",
                            stack: innerEx.StackTrace,
                            cancellationToken: cancellationToken);
                        return (null, false);
                    }
                }
                else
                {
                    _logger.LogError("Account creation conflict but no email to search by");
                    return (null, false);
                }
            }

            if (newAccount == null)
            {
                _logger.LogError("Failed to create account for {Provider} user {ProviderId}", providerName, userInfo.ProviderId);
                return (null, false);
            }

            // Store the OAuth link (as string since Guid is a value type)
            await linkStore.SaveAsync(
                oauthLinkKey,
                newAccount.AccountId.ToString(),
                cancellationToken: cancellationToken);

            // Maintain reverse index for cleanup on account deletion
            await AddToAccountOAuthLinksIndexAsync(newAccount.AccountId, oauthLinkKey, cancellationToken);

            // Sync auth method to Account service for cross-service discovery
            await EnsureAuthMethodSyncedAsync(newAccount.AccountId, provider, userInfo.ProviderId, cancellationToken);

            _logger.LogInformation("Created new account {AccountId} and linked to {Provider} user {ProviderId}",
                newAccount.AccountId, providerName, userInfo.ProviderId);

            return (newAccount, isNewAccount);
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
            return (null, false);
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

            case Provider.Steam:
                // Steam uses session tickets, not OAuth authorization URLs
                _logger.LogWarning("Steam does not support OAuth authorization URLs - use /auth/steam/verify with session tickets");
                return null;

            default:
                _logger.LogWarning("Unknown OAuth provider: {Provider}", provider);
                return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OAuthUserInfo> GetMockUserInfoAsync(Provider provider, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.GetMockUserInfo");
        var mockProviderId = provider switch
        {
            Provider.Discord => _configuration.MockDiscordId,
            Provider.Google => _configuration.MockGoogleId,
            Provider.Twitch => _configuration.MockTwitchId,
            Provider.Steam => _configuration.MockSteamId,
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
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.GetMockSteamUserInfo");
        await Task.CompletedTask;
        // MockSteamId has a default value in schema - no fallback needed
        var mockId = _configuration.MockSteamId;
        var suffix = mockId.Length >= 6 ? mockId.Substring(mockId.Length - 6) : mockId;
        return new OAuthUserInfo
        {
            ProviderId = mockId,
            Email = null,
            DisplayName = $"Steam_{suffix}"
        };
    }

    /// <summary>
    /// Gets the effective redirect URI for an OAuth provider.
    /// Uses explicit config if set, otherwise derives from ServiceDomain if available.
    /// </summary>
    /// <param name="configuredUri">The explicitly configured redirect URI (nullable)</param>
    /// <param name="provider">The provider name (discord, google, twitch)</param>
    /// <returns>The effective redirect URI, or null if neither is configured</returns>
    private string? GetEffectiveRedirectUri(string? configuredUri, string provider)
    {
        // If explicit redirect URI is configured, use it
        if (!string.IsNullOrWhiteSpace(configuredUri))
        {
            return configuredUri;
        }

        // If ServiceDomain is configured, derive the redirect URI from it
        var serviceDomain = _appConfiguration.ServiceDomain;
        if (!string.IsNullOrWhiteSpace(serviceDomain))
        {
            return $"https://{serviceDomain}/auth/oauth/{provider}/callback";
        }

        // Neither configured - return null to indicate provider should be disabled
        return null;
    }

    /// <inheritdoc/>
    public async Task CleanupOAuthLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.CleanupOAuthLinksForAccount");
        try
        {
            var indexKey = $"account-oauth-links:{accountId}";
            var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var linkKeys = await indexStore.GetAsync(indexKey, cancellationToken);

            if (linkKeys == null || linkKeys.Count == 0)
            {
                _logger.LogDebug("No OAuth links found for account {AccountId}", accountId);
                return;
            }

            var linkStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);

            foreach (var linkKey in linkKeys)
            {
                await linkStore.DeleteAsync(linkKey, cancellationToken);
            }

            // Remove the reverse index itself
            await indexStore.DeleteAsync(indexKey, cancellationToken);

            _logger.LogInformation("Cleaned up {Count} OAuth link(s) for deleted account {AccountId}",
                linkKeys.Count, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup OAuth links for account {AccountId}", accountId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "CleanupOAuthLinks",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Adds an OAuth link key to the account's reverse index for cleanup on deletion.
    /// </summary>
    private async Task AddToAccountOAuthLinksIndexAsync(Guid accountId, string oauthLinkKey, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.AddToAccountOAuthLinksIndex");
        try
        {
            var indexKey = $"account-oauth-links:{accountId}";
            var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);

            var existingLinks = await indexStore.GetAsync(indexKey, cancellationToken) ?? new List<string>();

            if (!existingLinks.Contains(oauthLinkKey))
            {
                existingLinks.Add(oauthLinkKey);
            }

            await indexStore.SaveAsync(indexKey, existingLinks, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal: link still works, just won't be cleaned up on account deletion
            _logger.LogWarning(ex, "Failed to add OAuth link to reverse index for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Ensures the OAuth auth method is registered in the Account service.
    /// This enables cross-service discovery (e.g., Achievement service finding Steam-linked accounts).
    /// Best-effort: auth flow succeeds even if this sync fails.
    /// </summary>
    private async Task EnsureAuthMethodSyncedAsync(Guid accountId, Provider provider, string externalId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OAuthProviderService.EnsureAuthMethodSynced");
        try
        {
            await _accountClient.AddAuthMethodAsync(new AddAuthMethodRequest
            {
                AccountId = accountId,
                Provider = MapProviderToOAuthProvider(provider),
                ExternalId = externalId
            }, cancellationToken);

            _logger.LogDebug("Auth method synced to Account service for {AccountId}, provider {Provider}",
                accountId, provider);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Already linked (idempotent) - expected on repeat logins
            _logger.LogDebug("Auth method already exists in Account service for {AccountId}, provider {Provider}",
                accountId, provider);
        }
        catch (Exception ex)
        {
            // Non-fatal: OAuth auth still works via Auth service's oauth-link keys.
            // Cross-service discovery (e.g., Achievement Steam sync) won't work until next successful login.
            _logger.LogWarning(ex,
                "Failed to sync auth method to Account service for {AccountId}, provider {Provider}",
                accountId, provider);
        }
    }

    /// <summary>
    /// Maps Auth service Provider enum to Account service OAuthProvider enum.
    /// </summary>
    private static OAuthProvider MapProviderToOAuthProvider(Provider provider)
    {
        return provider switch
        {
            Provider.Google => OAuthProvider.Google,
            Provider.Discord => OAuthProvider.Discord,
            Provider.Twitch => OAuthProvider.Twitch,
            Provider.Steam => OAuthProvider.Steam,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider has no OAuthProvider mapping")
        };
    }

    #region OAuth Response Models

    // External OAuth provider response DTOs below use string.Empty defaults as defensive coding.
    // These are third-party API responses where we have no control over the response format.

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
