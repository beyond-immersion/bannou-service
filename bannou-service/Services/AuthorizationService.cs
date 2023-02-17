using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for login authorization handling.
    /// </summary>
    [DaprService("Authorization Service", "authorization")]
    public class AuthorizationService : IDaprService
    {
        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/")]
        public async Task Authorize(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute($"/{ServiceConstants.SERVICE_UUID_PLACEHOLDER}")]
        public async Task AuthorizeDirect(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
