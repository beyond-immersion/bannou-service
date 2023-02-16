using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class ProfileService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"PROFILE_{Program.ServiceGUID}";

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/profile/create", CreateProfile);
            webApp.MapGet("/profile/update", UpdateProfile);
            webApp.MapGet("/profile/destroy", DestroyProfile);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task CreateProfile(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task UpdateProfile(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task DestroyProfile(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
