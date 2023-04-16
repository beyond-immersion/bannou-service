namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for player profile handling.
/// </summary>
[DaprService("profile")]
public class ProfileService : IDaprService
{
    /// <summary>
    /// Create new player profile.
    /// </summary>
    public async Task CreateProfile() => await Task.CompletedTask;

    /// <summary>
    /// Update existing player profile.
    /// </summary>
    public async Task UpdateProfile() => await Task.CompletedTask;

    /// <summary>
    /// Destroy existing player profile.
    /// </summary>
    public async Task DestroyProfile() => await Task.CompletedTask;
}
