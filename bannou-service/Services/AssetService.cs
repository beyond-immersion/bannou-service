using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for asset handling.
    /// </summary>
    [DaprService("Asset Service", "asset")]
    public class AssetService : IDaprService
    {
        /// <summary>
        /// List the assets in a given asset category.
        /// 
        /// List only includes metadata about the assets,
        /// not the content of the assets themselves.
        /// </summary>
        [ServiceRoute("/list")]
        public async Task ListAssets(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Create a new asset. Depending on the asset,
        /// and the manner of providing the data, this
        /// could take awhile.
        /// </summary>
        [ServiceRoute("/create")]
        public async Task CreateAsset(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Update the metadata or content or an existing
        /// asset.
        /// </summary>
        [ServiceRoute("/update")]
        public async Task UpdateAsset(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Destroy an existing asset.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task DestroyAsset(HttpContext context)
        {
            await Task.CompletedTask;
        }
    }
}
