using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class AuthorizationService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"AUTHORIZATION_{Program.ServiceGUID}";

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/authorization", Authorize);
            webApp.MapGet($"/authorization/{ServiceID}", AuthorizeDirect);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task Authorize(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task AuthorizeDirect(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
