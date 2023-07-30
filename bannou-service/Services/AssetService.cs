namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for asset handling.
/// </summary>
[DaprService("asset")]
public class AssetService : IDaprService
{
    /// <summary>
    /// List the assets in a given asset category.
    /// 
    /// List only includes metadata about the assets,
    /// not the content of the assets themselves.
    /// </summary>
    public async Task ListAssets() => await Task.CompletedTask;

    /// <summary>
    /// Create a new asset. Depending on the asset,
    /// and the manner of providing the data, this
    /// could take awhile.
    /// </summary>
    public async Task CreateAsset() => await Task.CompletedTask;

    /// <summary>
    /// Update the metadata or content or an existing
    /// asset.
    /// </summary>
    public async Task UpdateAsset() => await Task.CompletedTask;

    /// <summary>
    /// Destroy an existing asset.
    /// </summary>
    public async Task DestroyAsset() => await Task.CompletedTask;
}
