namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Data returned for successful logins.
/// </summary>
public class AccessData
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
}
