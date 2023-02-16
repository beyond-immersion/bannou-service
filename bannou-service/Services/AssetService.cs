using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class AssetService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"ASSET_{Program.ServiceGUID}";

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/asset/list", ListAssets);
            webApp.MapGet("/asset/create", CreateAsset);
            webApp.MapGet("/asset/update", UpdateAsset);
            webApp.MapGet("/asset/destroy", DestroyAsset);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task ListAssets(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task CreateAsset(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task UpdateAsset(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task DestroyAsset(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
