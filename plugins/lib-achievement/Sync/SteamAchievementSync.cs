using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Achievement.Sync;

/// <summary>
/// Steam achievement sync implementation using Steam Web API.
/// </summary>
/// <remarks>
/// <para>
/// Requires Steam Web API publisher key and app ID to be configured.
/// The Steam Web API requires the game to be published on Steam and
/// achievements to be configured in Steamworks.
/// </para>
/// <para>
/// Uses the publisher API endpoint (partner.steam-api.com) for setting
/// achievement and stat values. This requires publisher-level access
/// to the Steam Web API.
/// </para>
/// </remarks>
public class SteamAchievementSync : IPlatformAchievementSync
{
    private const string SteamPartnerApiBaseUrl = "https://partner.steam-api.com";
    private const string SetUserStatsEndpoint = "/ISteamUserStats/SetUserStatsForGame/v1/";

    private readonly AchievementServiceConfiguration _configuration;
    private readonly IAccountClient _accountClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SteamAchievementSync> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <inheritdoc />
    public Platform Platform => Platform.Steam;

    /// <inheritdoc />
    /// <remarks>
    /// Returns true only if both SteamApiKey and SteamAppId are configured.
    /// When false, the service layer skips this platform during sync operations.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(_configuration.SteamApiKey) &&
        !string.IsNullOrEmpty(_configuration.SteamAppId);

    /// <summary>
    /// Initializes a new instance of the SteamAchievementSync.
    /// </summary>
    /// <param name="configuration">Achievement service configuration containing Steam API credentials.</param>
    /// <param name="accountClient">Client for querying account service for Steam link status.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients for Steam API calls.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SteamAchievementSync(
        AchievementServiceConfiguration configuration,
        IAccountClient accountClient,
        IHttpClientFactory httpClientFactory,
        ILogger<SteamAchievementSync> logger,
        ITelemetryProvider telemetryProvider)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _accountClient = accountClient ?? throw new ArgumentNullException(nameof(accountClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "SteamAchievementSync.IsLinkedAsync");
        _logger.LogDebug("Checking Steam link status for account {AccountId}", accountId);

        try
        {
            var authMethods = await _accountClient.GetAuthMethodsAsync(
                new GetAuthMethodsRequest { AccountId = accountId },
                ct);

            var hasSteam = authMethods.AuthMethods.Any(m => m.Provider == AuthProvider.Steam);

            _logger.LogDebug(
                "Account {AccountId} Steam link status: {IsLinked}",
                accountId,
                hasSteam);

            return hasSteam;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug(
                "Account {AccountId} not found when checking Steam link status",
                accountId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to check Steam link status for account {AccountId}",
                accountId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "SteamAchievementSync.GetExternalIdAsync");
        _logger.LogDebug("Getting Steam ID for account {AccountId}", accountId);

        try
        {
            var authMethods = await _accountClient.GetAuthMethodsAsync(
                new GetAuthMethodsRequest { AccountId = accountId },
                ct);

            var steamMethod = authMethods.AuthMethods.FirstOrDefault(m => m.Provider == AuthProvider.Steam);

            if (steamMethod is null)
            {
                _logger.LogDebug(
                    "No Steam authentication method linked for account {AccountId}",
                    accountId);
                return null;
            }

            var externalId = steamMethod.ExternalId;

            if (string.IsNullOrEmpty(externalId))
            {
                _logger.LogWarning(
                    "Steam authentication method exists for account {AccountId} but external ID is empty",
                    accountId);
                return null;
            }

            _logger.LogDebug(
                "Found Steam ID {SteamId} for account {AccountId}",
                externalId,
                accountId);

            return externalId;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug(
                "Account {AccountId} not found when getting Steam ID",
                accountId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to get Steam ID for account {AccountId}",
                accountId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> UnlockAsync(
        string externalUserId,
        string platformAchievementId,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "SteamAchievementSync.UnlockAsync");
        _logger.LogInformation(
            "Unlocking Steam achievement {AchievementId} for user {SteamId}",
            platformAchievementId,
            externalUserId);

        // Validate configuration (defense-in-depth; service layer checks IsConfigured first)
        var configValidation = ValidateConfiguration(out var apiKey, out var appId);
        if (configValidation is not null)
        {
            return configValidation;
        }

        try
        {
            // Build the request to Steam Web API
            // For achievements, we set the achievement value to 1 to unlock it
            using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = apiKey,
                ["steamid"] = externalUserId,
                ["appid"] = appId,
                ["count"] = "1",
                ["name[0]"] = platformAchievementId,
                ["value[0]"] = "1" // 1 = unlocked
            });

            var result = await CallSteamApiAsync(formContent, ct);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Successfully unlocked Steam achievement {AchievementId} for user {SteamId}",
                    platformAchievementId,
                    externalUserId);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to unlock Steam achievement {AchievementId} for user {SteamId}: {Error}",
                    platformAchievementId,
                    externalUserId,
                    result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while unlocking Steam achievement {AchievementId} for user {SteamId}",
                platformAchievementId,
                externalUserId);

            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = $"Steam API call failed: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> SetProgressAsync(
        string externalUserId,
        string platformAchievementId,
        int current,
        int target,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "SteamAchievementSync.SetProgressAsync");
        _logger.LogInformation(
            "Setting Steam progress {Current}/{Target} for stat {StatId} for user {SteamId}",
            current,
            target,
            platformAchievementId,
            externalUserId);

        // Validate configuration (defense-in-depth; service layer checks IsConfigured first)
        var configValidation = ValidateConfiguration(out var apiKey, out var appId);
        if (configValidation is not null)
        {
            return configValidation;
        }

        try
        {
            // Steam uses stats for progress tracking
            // When a stat reaches a threshold configured in Steamworks,
            // Steam automatically unlocks the associated achievement
            using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = apiKey,
                ["steamid"] = externalUserId,
                ["appid"] = appId,
                ["count"] = "1",
                ["name[0]"] = platformAchievementId,
                ["value[0]"] = current.ToString()
            });

            var result = await CallSteamApiAsync(formContent, ct);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Successfully set Steam stat {StatId} to {Value} for user {SteamId}",
                    platformAchievementId,
                    current,
                    externalUserId);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to set Steam stat {StatId} for user {SteamId}: {Error}",
                    platformAchievementId,
                    externalUserId,
                    result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while setting Steam stat {StatId} for user {SteamId}",
                platformAchievementId,
                externalUserId);

            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = $"Steam API call failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Validates that the required Steam API configuration is present and returns the validated credentials.
    /// </summary>
    /// <param name="apiKey">The validated API key when configuration is valid.</param>
    /// <param name="appId">The validated App ID when configuration is valid.</param>
    /// <returns>
    /// A failure result if Steam sync is not configured, or null if configuration is valid and sync should proceed.
    /// The service layer checks IsConfigured before calling sync operations, so this is defense-in-depth.
    /// </returns>
    private PlatformSyncResult? ValidateConfiguration(out string apiKey, out string appId)
    {
        var configApiKey = _configuration.SteamApiKey;
        var configAppId = _configuration.SteamAppId;

        if (string.IsNullOrEmpty(configApiKey) || string.IsNullOrEmpty(configAppId))
        {
            _logger.LogError("Steam sync called but API credentials are not configured");
            apiKey = string.Empty;
            appId = string.Empty;
            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = "Steam API credentials not configured (SteamApiKey and SteamAppId required)"
            };
        }

        apiKey = configApiKey;
        appId = configAppId;
        return null;
    }

    /// <summary>
    /// Makes the actual HTTP call to Steam Web API.
    /// </summary>
    /// <param name="content">Form-encoded content to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the API call.</returns>
    private async Task<PlatformSyncResult> CallSteamApiAsync(
        FormUrlEncodedContent content,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "SteamAchievementSync.CallSteamApiAsync");
        using var httpClient = _httpClientFactory.CreateClient("SteamApi");

        // Build the full URL
        var requestUrl = $"{SteamPartnerApiBaseUrl}{SetUserStatsEndpoint}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = content
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogDebug("Sending request to Steam API: {Url}", requestUrl);

        var response = await httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogDebug(
            "Steam API response: Status={StatusCode}, Body={Body}",
            (int)response.StatusCode,
            responseBody);

        // Handle rate limiting
        if ((int)response.StatusCode == 429)
        {
            _logger.LogWarning("Steam API rate limit exceeded");
            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = "Steam API rate limit exceeded - retry later"
            };
        }

        // Handle other HTTP errors
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Steam API returned error status {StatusCode}: {Body}",
                (int)response.StatusCode,
                responseBody);

            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = $"Steam API returned status {(int)response.StatusCode}: {GetErrorFromResponse(responseBody)}"
            };
        }

        // Parse response
        return ParseSteamResponse(responseBody);
    }

    /// <summary>
    /// Parses the Steam API response JSON.
    /// </summary>
    /// <param name="responseBody">The JSON response body.</param>
    /// <returns>Result of parsing the response.</returns>
    private PlatformSyncResult ParseSteamResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("response", out var responseElement))
            {
                return new PlatformSyncResult
                {
                    Success = false,
                    ErrorMessage = "Steam API returned unexpected response format (missing 'response' field)"
                };
            }

            // Check result code - 1 means success
            if (responseElement.TryGetProperty("result", out var resultElement))
            {
                var result = resultElement.GetInt32();

                if (result == 1)
                {
                    return new PlatformSyncResult
                    {
                        Success = true,
                        SyncId = $"steam-{DateTime.UtcNow:yyyyMMddHHmmss}"
                    };
                }

                // Check for error details
                var errorMessage = "Unknown error";
                if (responseElement.TryGetProperty("error", out var errorElement))
                {
                    var error = errorElement.GetString();
                    if (!string.IsNullOrEmpty(error))
                    {
                        errorMessage = error;
                    }
                }

                return new PlatformSyncResult
                {
                    Success = false,
                    ErrorMessage = $"Steam API returned error result ({result}): {errorMessage}"
                };
            }

            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = "Steam API returned unexpected response format (missing 'result' field)"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Steam API response: {Body}", responseBody);

            return new PlatformSyncResult
            {
                Success = false,
                ErrorMessage = $"Failed to parse Steam API response: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Attempts to extract an error message from a response body.
    /// </summary>
    /// <param name="responseBody">The response body to parse.</param>
    /// <returns>Error message or a truncated version of the response body.</returns>
    private static string GetErrorFromResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (doc.RootElement.TryGetProperty("response", out var responseElement) &&
                responseElement.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString();
                if (!string.IsNullOrEmpty(error))
                {
                    return error;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors, fall through to truncated response
        }

        // Return truncated response body if we can't parse it
        return responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
    }
}
