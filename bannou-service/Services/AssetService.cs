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
        /// 
        /// </summary>
        [ServiceRoute(HttpMethodTypes.POST, "/list")]
        [ServiceRoute(HttpMethodTypes.GET, "/list")]
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
