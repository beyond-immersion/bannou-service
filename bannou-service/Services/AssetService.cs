using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for asset handling.
    /// </summary>
    [DaprService("Asset Service", "asset")]
    public class AssetService : IDaprService
    {
        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/list")]
        public async Task ListAssets(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/create")]
        public async Task CreateAsset(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/update")]
        public async Task UpdateAsset(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task DestroyAsset(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
