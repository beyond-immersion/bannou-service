using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for asset handling.
/// </summary>
[DaprController("asset")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AssetController : Controller, IDaprController<AssetService>
{
    /// <summary>
    /// List the assets in a given asset category.
    /// 
    /// List only includes metadata about the assets,
    /// not the content of the assets themselves.
    /// </summary>
    [DaprRoute("/list")]
    public async Task ListAssets(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Create a new asset. Depending on the asset,
    /// and the manner of providing the data, this
    /// could take awhile.
    /// </summary>
    [DaprRoute("/create")]
    public async Task CreateAsset(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update the metadata or content or an existing
    /// asset.
    /// </summary>
    [DaprRoute("/update")]
    public async Task UpdateAsset(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Destroy an existing asset.
    /// </summary>
    [DaprRoute("/destroy")]
    public async Task DestroyAsset(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
